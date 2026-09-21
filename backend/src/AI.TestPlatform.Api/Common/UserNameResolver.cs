using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Common;

/// <summary>
/// 批量解析用户显示名（M8 审计字段「创建人/修改人」在列表里展示用）。
///
/// 列表的创建人是 **CreatedById（Guid?，无导航）**——为避免给 9 个实体各加一条 User 导航/FK，
/// 统一用"收集本页 id → 一次查询 → 字典回填"的方式解析，每页只多一次查询。
/// </summary>
public static class UserNameResolver
{
    public static async Task<Dictionary<Guid, string>> ResolveAsync(
        TestDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();

        return await db.Users.AsNoTracking()
            .Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName!, ct);
    }

    /// <summary>从字典取显示名；取不到返回 null（历史行没有创建人时显示空，而非误导性的名字）</summary>
    public static string? GetName(this IReadOnlyDictionary<Guid, string> names, Guid? id) =>
        id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;
}
