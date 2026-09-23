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
    /// ⚠ 边界：`ExecuteUpdateAsync` / `ExecuteDeleteAsync` 绕过 SaveChanges，**不会**自动被本拦截器盖章。
    /// 历史上有批量写确实只改计数/状态（如 Execution 状态机），但**也有批量写直接改了审计实体却只写 UpdatedAt、漏写 UpdatedById**
    /// （见 TestPlanLinker.cs / TestPlanApiExtensions.cs 的测试计划范围编辑）。这类位点必须用
    /// <see cref="AuditBulk.Stamp{T}"/> 显式盖章，否则「谁改的」会丢失。新增批量写审计实体时，务必接 <c>Stamp</c>。
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
                // 只补"没写过的"：与下面 CreatedAt 同一条规则。显式指定创建人的位点
                // （种子数据、后台/Worker 以系统身份写入）不能被上下文里的当前用户覆盖；
                // 而**没有** HTTP 上下文时 userId 为 null，若无条件写入会把非空 Guid 列
                // 打成 Guid.Empty —— 直接触发外键违约（整表插入失败，且报错只说 FK 不说原因）。
                if (userId is { } uid && IsUnsetGuid(props.CreatedById?.GetValue(entry.Entity)))
                    props.CreatedById?.SetValue(entry.Entity, uid);
                // 只补"没写过的"：实体若已显式 stamp 了 CreatedAt，就不能覆盖成 SaveChanges 这一刻。
                // 否则业务上更早设置的字段会晚于它——AgentAttempt 在自愈循环里记 CreatedAt（尝试开始），
                // 循环结束才 SaveChanges，被覆盖后 CreatedAt 反而晚于 CompletedAt，耗时算出来是负数
                // （自愈度量 avgFixMinutes 因此恒为负）。
                if (IsUnset(props.CreatedAt?.GetValue(entry.Entity)))
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

    /// <summary>CreatedAt 是否尚未写入（DateTime 看 default，DateTime? 看 null）</summary>
    private static bool IsUnset(object? value) => value switch
    {
        null => true,
        DateTime dt => dt == default,
        _ => false,
    };

    /// <summary>CreatedById 是否尚未写入（Guid 看 default，Guid? 看 null）</summary>
    private static bool IsUnsetGuid(object? value) => value switch
    {
        null => true,
        Guid id => id == Guid.Empty,
        _ => false,
    };
}
