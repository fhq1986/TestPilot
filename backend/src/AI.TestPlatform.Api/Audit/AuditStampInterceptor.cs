using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AI.TestPlatform.Api.Audit;

/// <summary>
/// 审计盖章拦截器：在 SaveChanges 时给**带审计字段的实体**统一写入
/// 创建人 / 创建时间 / 修改人 / 修改时间。
///
/// 为什么用拦截器而不是在每个 create/update 端点里手写：
/// - **口径只有一处**：9 个模块 × 新增/修改两条路径 = 十几个写入点，手写必然漏；
/// - **零侵入**：实体只需"恰好有这些属性"，不需要实现接口、不需要继承基类，
///   因此不会因为 Project.CreatedById 是非空 Guid 之类的差异而扯出一串改动；
/// - **取人取的是 JWT**：与 CurrentUser 同源（NameIdentifier 声明），不查库。
///
/// 属性按**名字约定**匹配（CreatedById / CreatedAt / UpdatedById / UpdatedAt），
/// PropertyInfo 按 Type 缓存（ConcurrentDictionary），热路径上没有反射查找开销。
///
/// ⚠ 边界：`ExecuteUpdateAsync` / `ExecuteDeleteAsync` 绕过 SaveChanges，**不会**被盖章
/// （全库搜一遍可确认这两类调用都是计数/状态类操作，不带审计语义）。
/// </summary>
public sealed class AuditStampInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>各属性的缓存槽位（避免每次反射查找）</summary>
    private static readonly ConcurrentDictionary<Type, AuditProps> PropCache = new();

    private sealed record AuditProps(
        PropertyInfo? CreatedById, PropertyInfo? CreatedAt,
        PropertyInfo? UpdatedById, PropertyInfo? UpdatedAt);

    public AuditStampInterceptor(IHttpContextAccessor accessor) => _accessor = accessor;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var userId = ResolveUserId();
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (entry.Entity.GetType().Namespace?.StartsWith("AI.TestPlatform") != true) continue;

            var props = PropCache.GetOrAdd(entry.Entity.GetType(), t => new AuditProps(
                t.GetProperty("CreatedById"),
                t.GetProperty("CreatedAt"),
                t.GetProperty("UpdatedById"),
                t.GetProperty("UpdatedAt")));

            if (entry.State == EntityState.Added)
            {
                props.CreatedById?.SetValue(entry.Entity, userId);
                props.CreatedAt?.SetValue(entry.Entity, now);
            }
            // 修改时间/人：新增的也一并写（创建即视为最后修改），修改的只写修改
            props.UpdatedById?.SetValue(entry.Entity, userId);
            props.UpdatedAt?.SetValue(entry.Entity, now);
        }
    }

    private Guid? ResolveUserId()
    {
        var raw = _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
