using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

/// <summary>M8 Phase 3：Planner——按用例目标 + 失败历史重构步骤序列（见设计 §3 / §13 Phase 3）。</summary>
public interface IPlannerService
{
    Task<PlanResultDto> PlanAsync(Guid executionId, CancellationToken ct);
}

/// <summary>
/// 基于一次**失败执行**规划步骤序列：把用例目标 + 失败上下文 + 此前尝试交给 AIWorker 的 `/api/plan-steps`，
/// 返回可直接采纳的步骤序列（不自动落库——新步骤的落库属"采纳"流程）。
/// </summary>
public class AgentPlannerService : IPlannerService
{
    private readonly TestDbContext _db;
    private readonly AIClient _ai;
    private readonly ILogger<AgentPlannerService> _logger;

    public AgentPlannerService(TestDbContext db, AIClient ai, ILogger<AgentPlannerService> logger)
    {
        _db = db;
        _ai = ai;
        _logger = logger;
    }

    public async Task<PlanResultDto> PlanAsync(Guid executionId, CancellationToken ct)
    {
        var execution = await _db.Executions.AsNoTracking()
            .Include(e => e.TestCase).ThenInclude(t => t!.Steps)
            .Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution?.TestCase is null)
            return new PlanResultDto(Array.Empty<GeneratedCaseDto>(), 0f, ["执行或用例不存在"]);

        var testCase = execution.TestCase;

        var requirement = string.IsNullOrWhiteSpace(testCase.Description)
            ? testCase.Name
            : $"{testCase.Name}\n{testCase.Description}";

        var failed = execution.Results
            .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .OrderBy(r => r.StepOrder)
            .Take(10)
            .ToList();
        var failureContext = failed.Count == 0
            ? null
            : string.Join("\n", failed.Select(f =>
                $"步骤{f.StepOrder} 失败：{(string.IsNullOrWhiteSpace(f.ErrorMessage) ? "未知" : f.ErrorMessage)}"));

        // 此前步骤 + 已有 Agent 尝试：让 LLM 不要重复已经失败过的方案
        var previous = new List<object>
        {
            new
            {
                steps = testCase.Steps.OrderBy(s => s.StepOrder)
                    .Select(s => new
                    {
                        order = s.StepOrder,
                        action = s.ActionType.ToString(),
                        description = s.AIElementDescription,
                    }).ToList(),
            },
        };
        var attempts = await _db.AgentAttempts.AsNoTracking()
            .Where(a => a.ExecutionId == executionId)
            .OrderBy(a => a.AttemptNumber)
            .Select(a => new { a.AttemptNumber, a.FixCategory, a.FixSummary })
            .ToListAsync(ct);
        foreach (var a in attempts)
            previous.Add(new
            {
                attempt = a.AttemptNumber,
                fixCategory = a.FixCategory.ToString(),
                fixSummary = a.FixSummary,
            });

        // 熔断已下沉到 AIClient（统一入口），这里只做优雅降级
        PlanResultDto? plan;
        try
        {
            plan = await _ai.PlanAsync(requirement, testCase.BaseUrl, failureContext, previous, ct);
        }
        catch (AIWorkerException ex)
        {
            _logger.LogInformation("Planner 调用失败：{Message}", ex.Message);
            plan = null;
        }

        if (plan is null)
        {
            _logger.LogInformation("Planner 未产出（AI 不可用或熔断）execution={ExecutionId}", executionId);
            return new PlanResultDto(Array.Empty<GeneratedCaseDto>(), 0f,
                ["AI 不可用（熔断或调用失败），暂时无法规划"]);
        }
        return plan;
    }
}
