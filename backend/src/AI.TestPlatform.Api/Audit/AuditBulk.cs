using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace AI.TestPlatform.Api.Audit;

/// <summary>
/// 批量写审计盖章辅助。
///
/// <see cref="AuditStampInterceptor"/> 只在 <c>SaveChanges</c> 时盖章，
/// 而 <c>ExecuteUpdateAsync</c> / <c>ExecuteDeleteAsync</c> 绕过 SaveChanges，
/// 导致「只写 UpdatedAt、漏写 UpdatedById」这类不一致——本类就是为了堵这个洞。
///
/// 用法（与 SaveChanges 盖章同一口径）：
/// <code>
/// var now = DateTime.UtcNow;
/// var userId = db.ResolveAuditUserId();
/// await db.TestPlans.Where(p => p.Id == id)
///     .ExecuteUpdateAsync(s => s.Stamp(userId, now), ct);
/// </code>
///
/// <see cref="Stamp{T}"/> 对没有审计字段的实体类型是安全的（直接原样返回），
/// 因此可放心接到任意实体的批量更新上，避免重复踩坑。
/// </summary>
public static class AuditBulk
{
    /// <summary>审计字段的缓存槽位（按 Type，热路径无反射开销）。</summary>
    private static readonly ConcurrentDictionary<Type, (PropertyInfo? UpdatedAt, PropertyInfo? UpdatedById)> PropCache = new();

    /// <summary>
    /// 从 DbContext 解析当前操作人。与 <see cref="AuditStampInterceptor"/> 同源
    /// （<c>IHttpContextAccessor</c> → <c>NameIdentifier</c> 声明），后台无 HttpContext 时返回 null（系统操作）。
    /// </summary>
    public static Guid? ResolveAuditUserId(this DbContext db)
    {
        var sp = db.GetInfrastructure<IServiceProvider>();
        var raw = sp.GetService<IHttpContextAccessor>()?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// 在批量更新链上追加审计字段盖章（UpdatedAt + UpdatedById）。
    /// 只处理已知的两种审计字段类型（DateTime / Guid?），对 9 个审计实体都成立。
    /// </summary>
    public static SetPropertyCalls<T> Stamp<T>(this SetPropertyCalls<T> set, Guid? userId, DateTime now)
        where T : class
    {
        var (at, by) = PropCache.GetOrAdd(typeof(T), t => (
            t.GetProperty("UpdatedAt"),
            t.GetProperty("UpdatedById")));

        if (at is null && by is null) return set;

        // SetProperty 接收属性读取委托（Func<实体, 值>），用反射构造避免在每个调用站点手写字段
        if (at is not null)
        {
            var getter = (Func<T, DateTime>)Delegate.CreateDelegate(typeof(Func<T, DateTime>), at.GetGetMethod()!);
            set = set.SetProperty(getter, now);
        }
        if (by is not null)
        {
            var getter = (Func<T, Guid?>)Delegate.CreateDelegate(typeof(Func<T, Guid?>), by.GetGetMethod()!);
            set = set.SetProperty(getter, userId);
        }
        return set;
    }
}
