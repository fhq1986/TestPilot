using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.TestCases;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestCases;

// 用例版本历史端点（/api/testcases/{id}/versions）：
// 历史列表 / 单版本快照 / 回滚到某版本
public static class TestCaseVersionApiExtensions
{
    public static RouteGroupBuilder MapTestCaseVersionApi(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/versions", async (
            Guid id, TestDbContext db, TestCaseVersionService versions, CancellationToken ct) =>
        {
            if (!await db.TestCases.AnyAsync(t => t.Id == id, ct))
                return Results.NotFound();

            var rows = await versions.ListAsync(id, ct);
            return Results.Ok(rows.Select(v => new TestCaseVersionSummaryDto(
                v.Version, v.CreatedAt, v.OperatorName, v.ChangeSummary, v.StepCount)).ToList());
        }).WithPermission(Permission.ViewTestCases).Produces<List<TestCaseVersionSummaryDto>>();

        // 单版本快照。返回完整内容，前端的"与当前对比"直接拿它跟当前内容比，
        // 不另做 diff 接口——快照本身就不大，多一个接口只会多一套口径
        group.MapGet("/{id:guid}/versions/{version:int}", async (
            Guid id, int version, TestCaseVersionService versions, CancellationToken ct) =>
        {
            var row = await versions.GetAsync(id, version, ct);
            if (row is null) return Results.NotFound();

            var snapshot = TestCaseSnapshotExtensions.Deserialize(row.Snapshot);
            if (snapshot is null)
                return Results.BadRequest(new { message = $"第 {version} 版的快照无法解析（可能是旧格式）" });

            return Results.Ok(new TestCaseVersionDetailDto(
                row.Version, row.CreatedAt, row.OperatorName, row.ChangeSummary, snapshot));
        }).WithPermission(Permission.ViewTestCases).Produces<TestCaseVersionDetailDto>();

        // 回滚。回滚前会先记录当前内容，所以历史只追加、回滚本身也能被回滚
        group.MapPost("/{id:guid}/versions/{version:int}/restore", async (
            Guid id, int version, TestDbContext db, TestCaseVersionService versions, CancellationToken ct) =>
        {
            var testCase = await db.TestCases
                .Include(t => t.Steps.OrderBy(s => s.StepOrder)).ThenInclude(s => s.SharedGroup)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null) return Results.NotFound();

            var (ok, error) = await versions.RestoreAsync(testCase, version, ct);
            if (!ok) return Results.BadRequest(new { message = error });

            await db.SaveChangesAsync(ct);
            return Results.Ok(testCase.ToDto());
        }).WithPermission(Permission.ManageTestCases).WithAudit("RestoreVersion", "TestCase");

        // 批量删除历史版本（按版本号）。历史快照是纯留存数据：删除某版只失去那个回滚点，
        // 不影响当前用例内容，因此物理删除安全。清理用例改到面目全非后的一串中间版本时用。
        group.MapPost("/{id:guid}/versions/batch-delete", async (
            Guid id, BatchDeleteVersionsRequest request,
            TestDbContext db, TestCaseVersionService versions, CancellationToken ct) =>
        {
            if (!await db.TestCases.AnyAsync(t => t.Id == id, ct))
                return Results.NotFound(new { message = "用例不存在" });

            var versionNumbers = request.Versions.Distinct().OrderBy(v => v).ToList();
            if (versionNumbers.Count == 0)
                return Results.BadRequest(new { message = "请选择要删除的版本" });

            var rows = await db.TestCaseVersions
                .Where(v => v.TestCaseId == id && versionNumbers.Contains(v.Version))
                .ToListAsync(ct);
            if (rows.Count == 0)
                return Results.Json(new { message = "所选版本不存在" }, statusCode: 404);

            db.TestCaseVersions.RemoveRange(rows);
            await db.SaveChangesAsync(ct);

            var found = rows.Select(r => r.Version).ToHashSet();
            var skipped = versionNumbers.Where(v => !found.Contains(v)).ToList();
            return Results.Ok(new { deleted = rows.Count, skipped });
        }).WithPermission(Permission.ManageTestCases).WithAudit("DeleteVersions", "TestCase");

        return group;
    }
}

public record BatchDeleteVersionsRequest(List<int> Versions);
