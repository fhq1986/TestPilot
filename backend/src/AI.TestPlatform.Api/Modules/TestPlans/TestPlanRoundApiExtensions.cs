using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestPlans;

// 计划的轮次、达标判定与报告。与计划 CRUD 同属 /api/test-plans 分组
public static class TestPlanRoundApiExtensions
{
    public static RouteGroupBuilder MapTestPlanRoundApi(this RouteGroupBuilder group)
    {
        // ---------------------------------------------------------------- 轮次

        group.MapPost("/{id:guid}/rounds", async (Guid id, StartRoundRequest? request,
            TestPlanService plans, ICurrentUser current, CancellationToken ct) =>
        {
            // 手动开轮：触发方式是「手动」。曾写死成 TriggerType.TestPlan，
            // 导致轮次与它产生的执行都显示成「测试计划」，看不出到底是人点的还是定时触发的。
            var (round, error) = await plans.StartRoundAsync(id,
                request ?? new StartRoundRequest(),
                TriggerType.Manual, null, current.Id, ct);

            if (round is null)
            {
                // 「已有进行中的轮次」是可预期的业务拒绝 → 409；其余是真错误 → 400
                var conflict = error?.Contains("进行中") == true;
                return Results.Json(new { message = error },
                    statusCode: conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest);
            }
            if (error is not null)
                return Results.Json(new { message = error, created = round.CreatedCount },
                    statusCode: StatusCodes.Status400BadRequest);

            return Results.Created($"/api/test-plans/{id}/rounds/{round.Id}",
                new { roundId = round.Id, roundNo = round.RoundNo, created = round.CreatedCount });
        }).WithPermission(Permission.ManageTestPlans).WithAudit("StartRound", "TestPlan");

        group.MapGet("/{id:guid}/rounds", async (Guid id, TestDbContext db, TestPlanService plans,
            CancellationToken ct, [FromQuery] int take = 30) =>
        {
            take = Math.Clamp(take, 1, 200);
            var rounds = await db.TestPlanRounds.AsNoTracking()
                .Where(r => r.PlanId == id)
                .OrderByDescending(r => r.RoundNo)
                .Take(take)
                .ToListAsync(ct);

            var counts = await plans.LoadRoundCountsAsync(rounds.Select(r => r.Id).ToList(), ct);
            var plan = await db.TestPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            var excludeFlaky = plan?.ExcludeFlakyFromFailure ?? true;

            return Results.Ok(rounds.Select(r =>
            {
                if (!counts.TryGetValue(r.Id, out var c)) c = RoundCounts.Empty;
                // 与达标判定同一口径（含 flaky 排除），否则会出现两个不一致的通过率
                var stats = TestPlanService.ToStats(c, excludeFlaky);
                return new PlanRoundSummaryDto(r.Id, r.RoundNo, r.Status, r.TriggerType,
                    r.TriggerSource, r.StartedAt, r.CompletedAt, r.CreatedCount, r.Error,
                    stats,
                    // 轮次达标标记用的是**当前计划**的目标口径；计划改了目标，历史轮次的判定也跟着变——
                    // 这是刻意的：达标是相对目标的，目标变了历史结论本就该重算
                    plan is null || c.Pending > 0 ? null : stats.PassRate >= plan.TargetPassRate);
            }).ToList());
        }).WithPermission(Permission.ViewTestPlans);

        group.MapGet("/rounds/{roundId:guid}", async (Guid roundId,
            TestDbContext db, TestPlanService plans, CancellationToken ct) =>
        {
            var round = await db.TestPlanRounds.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roundId, ct);
            if (round is null) return Results.NotFound();

            var cases = await plans.LoadRoundDetailAsync(roundId, ct);
            var counts = await plans.LoadRoundCountsAsync([roundId], ct);
            if (!counts.TryGetValue(roundId, out var c)) c = RoundCounts.Empty;
            var plan = await db.TestPlans.AsNoTracking()
                .Where(p => p.Id == round.PlanId)
                .Select(p => new { p.ExcludeFlakyFromFailure, p.TargetPassRate })
                .FirstOrDefaultAsync(ct);
            var excludeFlaky = plan?.ExcludeFlakyFromFailure ?? true;
            var stats = TestPlanService.ToStats(c, excludeFlaky);

            return Results.Ok(new
            {
                round = new PlanRoundSummaryDto(round.Id, round.RoundNo, round.Status,
                    round.TriggerType, round.TriggerSource, round.StartedAt, round.CompletedAt,
                    round.CreatedCount, round.Error, stats,
                    plan is null || c.Pending > 0 ? null : stats.PassRate >= plan.TargetPassRate),
                cases,
            });
        }).WithPermission(Permission.ViewTestPlans);

        group.MapPost("/rounds/{roundId:guid}/abort", async (Guid roundId,
            TestPlanService plans, CancellationToken ct) =>
        {
            var (ok, skipped, error) = await plans.AbortRoundAsync(roundId, ct);
            return ok
                ? Results.Ok(new { message = $"已中止，跳过 {skipped} 条未开始的执行", skipped })
                : Results.Json(new { message = error }, statusCode: StatusCodes.Status400BadRequest);
        }).WithPermission(Permission.ManageTestPlans).WithAudit("AbortRound", "TestPlan");

        // ---------------------------------------------------------------- 达标判定与报告

        // 供 CI 与前端共用。CI 用法见 docs/ci-integration.md
        group.MapGet("/{id:guid}/gate", async (Guid id, TestPlanService plans, CancellationToken ct) =>
        {
            var result = await plans.BuildGateAsync(id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithPermission(Permission.ViewTestPlans);

        group.MapGet("/{id:guid}/report", async (Guid id, TestDbContext db, TestPlanService plans,
            CancellationToken ct) =>
        {
            var plan = await db.TestPlans.AsNoTracking()
                .Include(p => p.Project)
                .Include(p => p.Owner)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();

            var rounds = await plans.LoadRoundsRawAsync(id, ct);
            var gate = PlanGateEvaluator.Evaluate(plan.Name, plan.ReleaseName,
                plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure,
                plan.GateMode, rounds);

            var trends = rounds.Select(r =>
            {
                var (passed, passRate, _, _, denom, _, _) =
                    PlanGateEvaluator.Judge(r, plan.TargetPassRate, plan.AllowErrors,
                        plan.ExcludeFlakyFromFailure);
                return new PlanRoundTrendDto(r.RoundNo, r.StartedAt, r.CompletedAt,
                    denom, r.Passed, r.Failed, r.Error, r.Skipped, passRate, passed);
            }).ToList();

            // 范围覆盖率按模块分组：验收时最常问「哪个模块最差」
            var roundIds = await db.TestPlanRounds.AsNoTracking()
                .Where(r => r.PlanId == id).Select(r => r.Id).ToListAsync(ct);
            var moduleRows = await db.Executions.AsNoTracking()
                .Where(e => e.PlanRoundId != null && roundIds.Contains(e.PlanRoundId!.Value))
                .Select(e => new
                {
                    Module = e.TestCase != null && e.TestCase.Module != null ? e.TestCase.Module : "(未分类)",
                    e.Status,
                    IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
                })
                .ToListAsync(ct);

            // 模块通过率同样套用计划的 flaky 排除口径：
            // 报告里出现「总通过率 75% 但模块都不到 75%」这种自相矛盾会让人失去信任
            var excludeFlakyForModules = plan.ExcludeFlakyFromFailure;
            var modules = moduleRows.GroupBy(m => m.Module)
                .Select(g =>
                {
                    var excluded = excludeFlakyForModules
                        ? g.Count(x => x.IsFlaky && x.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                        : 0;
                    var failed = g.Count(x => x.Status == ExecutionStatus.Failed)
                                 - (excludeFlakyForModules ? g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Failed) : 0);
                    var error = g.Count(x => x.Status == ExecutionStatus.Error)
                                - (excludeFlakyForModules ? g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Error) : 0);
                    var skipped = g.Count(x => x.Status == ExecutionStatus.Skipped);
                    var passed = g.Count(x => x.Status == ExecutionStatus.Passed);
                    var denominator = Math.Max(0, g.Count() - skipped - excluded);
                    return new PlanModuleStatDto(g.Key, denominator, passed, failed, error, skipped,
                        denominator > 0 ? (double)passed / denominator : 0);
                })
                .OrderByDescending(m => m.Failed + m.Error)
                .ThenBy(m => m.Module)
                .ToList();

            var ownerName = plan.Owner is null ? null : plan.Owner.DisplayName ?? plan.Owner.Username;
            var caseCount = await db.TestPlanItems.CountAsync(i => i.PlanId == id, ct);
            var summary = new TestPlanSummaryDto(plan.Id, plan.ProjectId, plan.Project.Name,
                plan.Name, plan.Description,
                plan.ReleaseName, plan.Status, plan.StartsAt, plan.EndsAt, plan.OwnerId, ownerName,
                plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure, plan.GateMode,
                plan.DefectGateEnabled,
                plan.EnvironmentId,
                caseCount, null, null, null, plan.LastRoundAt,
                trends.Count > 0 ? trends[^1].RoundNo : null,
                trends.Count > 0 ? trends[^1].PassRate : null,
                plan.LastError, plan.CreatedAt, plan.UpdatedAt);

            return Results.Ok(new TestPlanReportDto(summary, gate, trends, modules, gate.BlockingCases));
        }).WithPermission(Permission.ViewTestPlans);

        // 导出 xlsx：验收材料要有能带走的文件
        group.MapGet("/{id:guid}/export", async (Guid id, TestPlanReportService reports,
            CancellationToken ct) =>
        {
            var result = await reports.GenerateAsync(id, ct);
            if (result is null) return Results.NotFound();
            var (content, fileName) = result.Value;
            return Results.File(content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }).WithPermission(Permission.ViewTestPlans).WithAudit("Export", "TestPlan", captureBody: false);

        return group;
    }
}
