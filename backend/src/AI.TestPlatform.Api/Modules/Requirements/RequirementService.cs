using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Requirements;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Requirements;

/// <summary>
/// 需求覆盖统计服务（精简版）。
///
/// 口径说明：
/// - 「覆盖」= 该需求名下至少挂了一个**可见**用例（软删除过滤器自动生效）；
/// - 「验证进度」= 关联用例里最近一次执行已通过的数量——需求不只是"有人测"，
///   还要知道"测过了没有"，这是比覆盖率更进一步的信号；
/// - 数据量级：需求/用例都是百级，一次拉回内存聚合，避免逐需求 N+1 查询。
///
/// 「说明」是用户提交的富文本：**读侧也过一遍 <see cref="RichText.Normalize"/>**。
/// 写侧净化只能管住新数据，库里还躺着改版之前的历史纯文本（换行、且从未被净化过），
/// 它们马上要进富文本编辑器 / v-html——读侧这道关不做，老数据就直接变成一个存储型 XSS 口子。
/// </summary>
public class RequirementService(TestDbContext db)
{
    public async Task<PagedResult<RequirementListItemDto>> ListAsync(
        Guid? projectId, string? search, RequirementStatus? status, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Requirements.AsNoTracking()
            .Where(r => projectId == null || r.ProjectId == projectId)
            .Where(r => status == null || r.Status == status.Value)
            .Where(r => search == null ||
                        EF.Functions.ILike(r.Title, $"%{search}%") ||
                        (r.ExternalKey != null && EF.Functions.ILike(r.ExternalKey, $"%{search}%")));
        var total = await query.CountAsync(ct);

        var requirements = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.ProjectId, r.Title, r.Description, r.ExternalKey,
                r.Priority, r.CreatedAt, r.CreatedById,
                r.PlanStartDate, r.PlanEndDate, r.ActualStartDate, r.ActualEndDate,
                r.Status,
                PlanCount = r.TestPlans.Count,
                // 已删除的用例由 TestCase 的全局查询过滤器负责排除，这里不再写第二遍
                Cases = r.TestCases
                    .Select(t => new { t.Id, LastStatus = t.Executions
                        .OrderByDescending(e => e.CreatedAt)
                        .Select(e => (ExecutionStatus?)e.Status)
                        .FirstOrDefault() })
                    .ToList(),
            })
            .ToListAsync(ct);

        // 创建人显示名（M8 审计字段）：本页一次批量解析
        var creatorNames = await UserNameResolver.ResolveAsync(db, requirements.Select(r => r.CreatedById), ct);

        var items = requirements.Select(r => new RequirementListItemDto(
            r.Id, r.ProjectId, r.Title, RichText.Normalize(r.Description), r.ExternalKey,
            r.Priority, r.CreatedAt,
            r.Cases.Count,
            r.Cases.Count(c => c.LastStatus == ExecutionStatus.Passed),
            creatorNames.GetName(r.CreatedById),
            r.PlanStartDate, r.PlanEndDate, r.ActualStartDate, r.ActualEndDate,
            r.Status,
            r.PlanCount)).ToList();

        return new PagedResult<RequirementListItemDto>(items, total, page, pageSize);
    }

    /// <summary>项目级覆盖统计。uncovered=false 时 UncoveredList 为空（列表页自己有数据）</summary>
    public async Task<RequirementCoverageDto> CoverageAsync(Guid? projectId, CancellationToken ct)
    {
        var rows = await db.Requirements.AsNoTracking()
            .Where(r => projectId == null || r.ProjectId == projectId)
            .Select(r => new
            {
                r.Id, r.ProjectId, r.Title, r.Description, r.ExternalKey,
                r.Priority, r.CreatedAt, r.CreatedById,
                r.PlanStartDate, r.PlanEndDate, r.ActualStartDate, r.ActualEndDate,
                r.Status,
                CaseCount = r.TestCases.Count,
            })
            .ToListAsync(ct);

        var covered = rows.Count(r => r.CaseCount > 0);
        var uncovered = rows.Where(r => r.CaseCount == 0).ToList();

        // 创建人显示名（M8 审计字段）：本批一次批量解析
        var creatorNames = await UserNameResolver.ResolveAsync(db, rows.Select(r => r.CreatedById), ct);

        return new RequirementCoverageDto(
            projectId ?? rows.Select(r => r.ProjectId).Distinct().FirstOrDefault(),
            rows.Count,
            covered,
            uncovered.Count,
            rows.Count == 0 ? 0 : Math.Round(covered * 100.0 / rows.Count, 1),
            uncovered.Select(r => new RequirementListItemDto(
                r.Id, r.ProjectId, r.Title, RichText.Normalize(r.Description), r.ExternalKey,
                r.Priority, r.CreatedAt, 0, 0,
                creatorNames.GetName(r.CreatedById),
                r.PlanStartDate, r.PlanEndDate, r.ActualStartDate, r.ActualEndDate,
                r.Status, 0)).Take(20).ToList());
    }

    public async Task<RequirementListItemDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Requirements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;

        var cases = await db.TestCases.AsNoTracking()
            .Where(t => t.RequirementId == id)
            .Select(t => new { t.Id, t.Name, t.Module, t.IsFlaky, t.Status })
            .ToListAsync(ct);

        return new RequirementListItemDto(
            r.Id, r.ProjectId, r.Title, RichText.Normalize(r.Description), r.ExternalKey, r.Priority, r.CreatedAt,
            cases.Count, 0,
            PlanStartDate: r.PlanStartDate, PlanEndDate: r.PlanEndDate,
            ActualStartDate: r.ActualStartDate, ActualEndDate: r.ActualEndDate,
            Status: r.Status);
    }

    public async Task<Requirement> CreateAsync(CreateRequirementRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("需求标题不能为空");

        var requirement = new Requirement
        {
            ProjectId = request.ProjectId,
            Title = request.Title.Trim(),
            Description = RichText.Normalize(request.Description),
            ExternalKey = request.ExternalKey,
            Priority = request.Priority,
            PlanStartDate = ToUtc(request.PlanStartDate),
            PlanEndDate = ToUtc(request.PlanEndDate),
            ActualStartDate = ToUtc(request.ActualStartDate),
            ActualEndDate = ToUtc(request.ActualEndDate),
            Status = request.Status ?? RequirementStatus.NotStarted,
        };
        db.Requirements.Add(requirement);
        await db.SaveChangesAsync(ct);
        return requirement;
    }

    public async Task<Requirement?> UpdateAsync(Guid id, UpdateRequirementRequest request, CancellationToken ct)
    {
        var requirement = await db.Requirements.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (requirement is null) return null;

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("需求标题不能为空");
        requirement.Title = request.Title.Trim();
        requirement.Description = RichText.Normalize(request.Description);
        requirement.ExternalKey = request.ExternalKey;
        requirement.Priority = request.Priority;
        requirement.PlanStartDate = ToUtc(request.PlanStartDate);
        requirement.PlanEndDate = ToUtc(request.PlanEndDate);
        requirement.ActualStartDate = ToUtc(request.ActualStartDate);
        requirement.ActualEndDate = ToUtc(request.ActualEndDate);
        if (request.Status.HasValue) requirement.Status = request.Status.Value;
        await db.SaveChangesAsync(ct);
        return requirement;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var requirement = await db.Requirements.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (requirement is null) return false;
        db.Requirements.Remove(requirement);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>批量删除：需求 ↔ 用例的映射由级联清理，返回删除数与被跳过项。</summary>
    public async Task<BatchDeleteResultDto> DeleteManyAsync(List<Guid> ids, CancellationToken ct)
    {
        var distinctIds = ids.Distinct().ToList();
        var requirements = await db.Requirements.Where(r => distinctIds.Contains(r.Id)).ToListAsync(ct);
        db.Requirements.RemoveRange(requirements);

        var found = requirements.Select(r => r.Id).ToHashSet();
        var skipped = distinctIds.Where(id => !found.Contains(id))
            .Select(id => new BatchDeleteSkippedItem(id, null, "需求不存在"))
            .ToList();

        await db.SaveChangesAsync(ct);
        return new BatchDeleteResultDto(requirements.Count, skipped);
    }

    /// <summary>
    /// JSON 绑定的时间是 Kind=Unspecified（前端只传纯日期 "2026-09-21" 或无时区值），
    /// Npgsql 写 timestamptz 拒绝 Unspecified——按本地时间解释后转 UTC。
    /// 与 TestPlanApiExtensions / TestCaseReportService 的 ToUtc 同一套约定。
    /// </summary>
    private static DateTime? ToUtc(DateTime? value)
    {
        if (value is null) return null;
        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime(),
        };
    }
}
