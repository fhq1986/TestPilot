using System.Net;
using System.Net.Http.Json;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

/// <summary>
/// Agent 审批「采纳」的落库闭环（M8）。
///
/// 线上故障：点采纳提示成功，用例步骤却毫无变化。两个原因叠在一起——
/// 端点读了恒为空的 AppliedActions，且 add_step 只改内存 List 没进 EF 追踪。
/// 单测覆盖了差集/排序逻辑，这里补的是**最后一段 EF 落库**：没有它就无法证明
/// 「采纳后步骤真的进库」，而这正是用户看到的那个现象。
/// </summary>
[Collection("api")]
public class AgentApprovalTests
{
    private readonly TestApiFactory _factory;

    public AgentApprovalTests(TestApiFactory factory) => _factory = factory;

    private sealed record ApproveResult(int Applied, List<RejectedFixDto> Rejected);
    private sealed record RejectedFixDto(string ActionType, string Reason);

    /// <summary>线上真实提议：4 个 add_step（登录前置）。position 是字符串、Wait 用 timeout_ms。</summary>
    private const string RealProposedFixes = """
    [
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Fill","position":"0","value":"${username}","selector_type":"css","selector_value":"input[placeholder=\"用户名\"]"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Fill","position":"1","value":"${password}","selector_type":"css","selector_value":"input[placeholder=\"密码\"]"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Click","position":"2","selector_type":"css","selector_value":"button.login-btn"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Wait","position":"3","timeout_ms":"5000"},"confidence":0.6}
    ]
    """;

    [Fact]
    public async Task 采纳后提议动作真的写进用例步骤()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateCaseAsync(client, project.Id);
        var attemptId = await SeedPendingAttemptAsync(testCase.Id, RealProposedFixes);

        var response = await client.PostAsync($"/api/executions/agent-attempts/{attemptId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApproveResult>();

        Assert.Equal(4, result!.Applied);
        Assert.Empty(result.Rejected);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var steps = await db.TestSteps.AsNoTracking()
            .Where(s => s.TestCaseId == testCase.Id).OrderBy(s => s.StepOrder).ToListAsync();

        // 只改内存 List 的话这里仍是 3 条——就是线上那个"采纳成功但步骤没变"
        Assert.Equal(7, steps.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, steps.Select(s => s.StepOrder).ToArray());
        Assert.Equal(
            new[]
            {
                ActionType.Fill, ActionType.Fill, ActionType.Click, ActionType.Wait,
                ActionType.Navigate, ActionType.Wait, ActionType.AssertVisible,
            },
            steps.Select(s => s.ActionType).ToArray());

        // 新步骤必须真的落到目标用例下，且带上定位符/取值
        Assert.Equal("${username}", steps[0].Config.Value);
        Assert.Equal("input[placeholder=\"用户名\"]", steps[0].Config.Selector!.Value);
        Assert.Equal("5000", steps[3].Config.Value);

        var attempt = await db.AgentAttempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.True(attempt.Persisted, "采纳成功应标记 Persisted");
        Assert.True(attempt.Approved);
    }

    [Fact]
    public async Task 全部动作被安全校验拒绝时步骤不变且回传原因()
    {
        var client = await TestClientHelper.CreateAuthenticatedAsync(_factory);
        var project = await CreateProjectAsync(client);
        var testCase = await CreateCaseAsync(client, project.Id);
        var attemptId = await SeedPendingAttemptAsync(testCase.Id, """
        [{"actionType":"drop_database","stepOrder":1,"params":{},"confidence":0.9}]
        """);

        var response = await client.PostAsync($"/api/executions/agent-attempts/{attemptId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApproveResult>();

        Assert.Equal(0, result!.Applied);
        // 原因必须回传：原来端点 out _ 吞掉原因，界面只能说「无可用动作可应用」
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal("drop_database", rejected.ActionType);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var steps = await db.TestSteps.AsNoTracking()
            .Where(s => s.TestCaseId == testCase.Id).OrderBy(s => s.StepOrder).ToListAsync();
        Assert.Equal(3, steps.Count);
        Assert.Equal(new[] { 1, 2, 3 }, steps.Select(s => s.StepOrder).ToArray());

        var attempt = await db.AgentAttempts.AsNoTracking().SingleAsync(a => a.Id == attemptId);
        Assert.False(attempt.Persisted);
    }

    // ------------------------------------------------------------------ 辅助

    private static async Task<ProjectDto> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/projects",
            new { name = $"Project-{Guid.NewGuid():N}", description = (string?)null });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProjectDto>())!;
    }

    /// <summary>线上那条用例采纳前的三步：Navigate / Wait / AssertVisible</summary>
    private static async Task<TestCaseDto> CreateCaseAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync("/api/testcases", new
        {
            projectId,
            name = $"TC-{Guid.NewGuid():N}",
            type = 0,
            description = (string?)null,
            browser = "chromium",
            timeout = 30000,
            retryCount = 0,
            steps = new object[]
            {
                new { stepOrder = 1, actionType = 2, config = new { url = "/test-plans" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 2, actionType = 3, config = new { value = "3000" }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
                new { stepOrder = 3, actionType = 11, config = new { selector = new { type = "css", value = ".el-table", description = (string?)null } }, aiInstruction = (string?)null, aiElementDescription = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestCaseDto>())!;
    }

    /// <summary>
    /// 造一条「需人工审批」的 attempt。不跑真实自愈循环：审批分支本来就只在 LLM 判定
    /// 破坏性改动时才产生，用 LLM 桩去凑那条路径既脆弱又与被测逻辑无关。
    /// </summary>
    private async Task<Guid> SeedPendingAttemptAsync(Guid testCaseId, string proposedFixes)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var execution = new Execution
        {
            Id = Guid.NewGuid(),
            TestCaseId = testCaseId,
            Status = ExecutionStatus.Failed,
            TriggerType = TriggerType.Manual,
        };
        db.Executions.Add(execution);

        var attempt = new AgentAttempt
        {
            Id = Guid.NewGuid(),
            ExecutionId = execution.Id,
            AttemptNumber = 1,
            TargetStepOrder = 1,
            FixCategory = FixCategory.StepInsertion,
            Confidence = 0.85f,
            FixSummary = "应补登录前置步骤",
            NeedsApproval = true,
            ProposedFixes = proposedFixes,
            Result = AgentAttemptResult.Skipped,
        };
        db.AgentAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }
}
