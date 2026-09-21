using System.Text.Json;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// M8 Agent 闭环的契约测试（见 docs/m8-agent-design.md §5.1 / §12.1 / §16.5）。
/// 钉住：枚举 int 值只可追加、修复只改副本、非法动作被拒、熔断半开态正确。
/// </summary>
public class M8AgentTests
{
    // ------------------------------ 枚举数值契约（按 int 落库，只能追加） ------------------------------

    [Fact]
    public void FixCategory_数值契约()
    {
        Assert.Equal(0, (int)FixCategory.LocatorUpdate);
        Assert.Equal(1, (int)FixCategory.WaitStrategy);
        Assert.Equal(2, (int)FixCategory.StepConfigPatch);
        Assert.Equal(3, (int)FixCategory.StepInsertion);
        Assert.Equal(4, (int)FixCategory.StepDeletion);
        Assert.Equal(5, (int)FixCategory.StepReorder);
        Assert.Equal(6, (int)FixCategory.AssertRelaxation);
        Assert.Equal(7, (int)FixCategory.AppBug);
        Assert.Equal(8, (int)FixCategory.EnvironmentIssue);
        Assert.Equal(9, (int)FixCategory.DataIssue);
        Assert.Equal(10, (int)FixCategory.Unknown);
    }

    [Fact]
    public void AgentAttemptResult_数值契约()
    {
        Assert.Equal(0, (int)AgentAttemptResult.Fixed);
        Assert.Equal(1, (int)AgentAttemptResult.Partial);
        Assert.Equal(2, (int)AgentAttemptResult.Failed);
        Assert.Equal(3, (int)AgentAttemptResult.BudgetExhausted);
        Assert.Equal(4, (int)AgentAttemptResult.Rejected);
        Assert.Equal(5, (int)AgentAttemptResult.Skipped);
    }

    // ------------------------------ FixActionApplier ------------------------------

    private static TestStep Step(int order, string selectorValue) => new()
    {
        StepOrder = order,
        ActionType = ActionType.Click,
        Config = new StepConfig { Selector = new SelectorConfig { Type = "css", Value = selectorValue } },
    };

    private static FixActionDto UpdateLocator(int order, string value) => new(
        "update_locator", order,
        new Dictionary<string, object> { ["locator_type"] = "css", ["locator_value"] = value }, 0.9f);

    [Fact]
    public void 应用update_locator_只改副本不改原始步骤()
    {
        var original = Step(3, "button.login");
        // 模拟 AgentLoopService 的深拷贝（这里手工造一份独立副本）
        var copy = new List<TestStep> { Step(3, "button.login") };

        var ok = FixActionApplier.TryApply(copy, UpdateLocator(3, "button.login-btn"), out var error);

        Assert.True(ok, error);
        Assert.Equal("button.login-btn", copy[0].Config.Selector!.Value);
        // 原始步骤（代表 DB 实体）不得被改动
        Assert.Equal("button.login", original.Config.Selector!.Value);
    }

    [Fact]
    public void 拒绝不支持的动作()
    {
        var copy = new List<TestStep> { Step(1, "a") };
        // 早期只有 update_locator；现在 7 类都支持了，改用一个**从未实现**的动作名验证白名单兜底
        var action = new FixActionDto("inject_custom_script", 1, new Dictionary<string, object>(), 0.99f);

        Assert.False(FixActionApplier.TryApply(copy, action, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void 拒绝含注入字符的定位器()
    {
        var copy = new List<TestStep> { Step(1, "a") };
        Assert.False(FixActionApplier.TryApply(copy, UpdateLocator(1, "<script>alert(1)</script>"), out _));
    }

    [Fact]
    public void 找不到目标步骤时拒绝()
    {
        var copy = new List<TestStep> { Step(1, "a") };
        Assert.False(FixActionApplier.TryApply(copy, UpdateLocator(99, "b"), out var error));
        Assert.NotNull(error);
    }

    // ------------------------------ AILivenessBreaker ------------------------------

    private static AILivenessBreaker Breaker(int threshold, int cooldownMs) =>
        new(Options.Create(new AgentLoopOptions
        {
            ConsecutiveLlmFailuresThreshold = threshold,
            LlmCircuitCooldownMs = cooldownMs,
        }));

    [Fact]
    public void 熔断_连续失败达阈值后短路()
    {
        var breaker = Breaker(threshold: 3, cooldownMs: 60_000);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(breaker.AllowCall());   // Closed：放行
            breaker.OnFailure();
        }
        Assert.False(breaker.AllowCall());       // Open：短路
    }

    [Fact]
    public void 熔断_半开探针失败立即重开路()
    {
        var breaker = Breaker(threshold: 3, cooldownMs: 10);
        for (var i = 0; i < 3; i++) { breaker.AllowCall(); breaker.OnFailure(); }
        Assert.False(breaker.AllowCall());       // Open

        Thread.Sleep(40);                         // 过冷却期
        Assert.True(breaker.AllowCall());         // 半开：放行唯一探针
        Assert.False(breaker.AllowCall());        // 探针在途，其余拒绝

        breaker.OnFailure();                      // 探针失败 → 应**立即**重开路（不等阈值）
        Assert.False(breaker.AllowCall());
    }

    [Fact]
    public void 熔断_半开探针成功恢复健康()
    {
        var breaker = Breaker(threshold: 1, cooldownMs: 10);
        Assert.True(breaker.AllowCall());
        breaker.OnFailure();                      // threshold=1 → Open
        Assert.False(breaker.AllowCall());

        Thread.Sleep(40);
        Assert.True(breaker.AllowCall());         // 探针
        breaker.OnSuccess();                      // 成功 → Closed
        Assert.True(breaker.AllowCall());
    }

    // ------------------------------ 归因映射（AIClient.MapAttributedResult，HTTP 桩的可测核心） ------------------------------

    [Fact]
    public void 归因映射_解析fix_category与动作()
    {
        using var doc = JsonDocument.Parse("""
        {
          "category":"选择器失效","root_cause":"x","confidence":0.9,"suggested_fix":"y","retry_recommended":true,
          "fix_category":"LocatorUpdate",
          "proposed_fixes":[{"action_type":"update_locator","step_order":3,
            "params":{"locator_type":"css","locator_value":"button.b"},"confidence":0.8}],
          "needs_human_approval":false,"approval_reason":null
        }
        """);
        var r = AIClient.MapAttributedResult(doc.RootElement);

        Assert.Equal(FixCategory.LocatorUpdate, r.FixCategory);
        Assert.Single(r.ProposedFixes);
        Assert.Equal("update_locator", r.ProposedFixes[0].ActionType);
        Assert.Equal(3, r.ProposedFixes[0].StepOrder);
        Assert.True(r.RetryRecommended);
        Assert.False(r.NeedsHumanApproval);
    }

    [Fact]
    public void 归因映射_未知fix_category兜底为Unknown()
    {
        using var doc = JsonDocument.Parse("""
        {"category":"其它","confidence":0.2,"fix_category":"NotAReal","needs_human_approval":false}
        """);
        var r = AIClient.MapAttributedResult(doc.RootElement);
        Assert.Equal(FixCategory.Unknown, r.FixCategory);
        Assert.Empty(r.ProposedFixes);
    }

    [Fact]
    public void 归因映射_缺category抛AIWorker异常()
    {
        using var doc = JsonDocument.Parse("""{"confidence":0.5}""");
        Assert.Throws<AIWorkerException>(() => AIClient.MapAttributedResult(doc.RootElement));
    }

    // ------------------------------ ① FixActionApplier 扩展动作 ------------------------------

    private static List<TestStep> Steps(params string[] selectorValues) =>
        selectorValues.Select((v, i) => Step(i, v)).ToList();

    private static FixActionDto Action(string type, int? order, Dictionary<string, object>? p = null) =>
        new(type, order, p ?? new Dictionary<string, object>(), 0.9f);

    [Fact]
    public void 动作支持集与破坏性判定()
    {
        Assert.Contains("update_locator", FixActionApplier.SupportedActions);
        Assert.Contains("step_config_patch", FixActionApplier.SupportedActions);
        Assert.Contains("wait_strategy", FixActionApplier.SupportedActions);
        Assert.Contains("relax_assert", FixActionApplier.SupportedActions);
        Assert.Contains("add_step", FixActionApplier.SupportedActions);
        Assert.Contains("delete_step", FixActionApplier.SupportedActions);
        Assert.Contains("reorder_step", FixActionApplier.SupportedActions);

        Assert.True(FixActionApplier.IsDestructive("delete_step"));
        Assert.True(FixActionApplier.IsDestructive("reorder_step"));
        Assert.True(FixActionApplier.IsDestructive("add_step"));
        Assert.True(FixActionApplier.IsDestructive("relax_assert"));
        Assert.False(FixActionApplier.IsDestructive("update_locator"));
        Assert.False(FixActionApplier.IsDestructive("wait_strategy"));
    }

    [Fact]
    public void 动作_step_config_patch_改配置字段()
    {
        var steps = Steps("a");
        var ok = FixActionApplier.TryApply(steps, Action("step_config_patch", 0,
            new() { ["field"] = "value", ["value"] = "hello" }), out var err);
        Assert.True(ok, err);
        Assert.Equal("hello", steps[0].Config.Value);
    }

    [Fact]
    public void 动作_step_config_patch_拒绝越界URL()
    {
        var steps = Steps("a");
        var action = Action("step_config_patch", 0,
            new() { ["field"] = "url", ["value"] = "http://evil.example.com/x" });

        Assert.False(FixActionApplier.TryApply(steps, action, out var err, "http://127.0.0.1:8899/"));
        Assert.NotNull(err);

        // 相对路径合法
        var ok = FixActionApplier.TryApply(steps, Action("step_config_patch", 0,
            new() { ["field"] = "url", ["value"] = "/login" }), out _, "http://127.0.0.1:8899/");
        Assert.True(ok);
    }

    [Fact]
    public void 动作_wait_strategy_在目标前插入Wait()
    {
        var steps = Steps("a", "b");
        var ok = FixActionApplier.TryApply(steps, Action("wait_strategy", 1,
            new() { ["timeout_ms"] = 3000 }), out var err);

        Assert.True(ok, err);
        Assert.Equal(3, steps.Count);
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(ActionType.Wait, ordered[1].ActionType);
        Assert.Equal("3000", ordered[1].Config.Value);
        // 重排后 StepOrder 连续
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void 动作_delete_step_删除并重排()
    {
        var steps = Steps("s0", "s1", "s2");
        var ok = FixActionApplier.TryApply(steps, Action("delete_step", 1), out var err);

        Assert.True(ok, err);
        Assert.Equal(2, steps.Count);
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(new[] { "s0", "s2" }, ordered.Select(s => s.Config.Selector!.Value).ToArray());
        Assert.Equal(new[] { 0, 1 }, ordered.Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void 动作_reorder_step_移动并重排()
    {
        var steps = Steps("s0", "s1", "s2");
        var ok = FixActionApplier.TryApply(steps, Action("reorder_step", 0,
            new() { ["to_order"] = 2 }), out var err);

        Assert.True(ok, err);
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(new[] { "s1", "s2", "s0" }, ordered.Select(s => s.Config.Selector!.Value).ToArray());
    }

    [Fact]
    public void 动作_add_step_插入新步骤()
    {
        var steps = Steps("s0", "s1");
        var ok = FixActionApplier.TryApply(steps, Action("add_step", null, new()
        {
            ["action_type"] = "Wait",
            ["value"] = "500",
            ["position"] = 1,
        }), out var err);

        Assert.True(ok, err);
        Assert.Equal(3, steps.Count);
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        Assert.Equal(ActionType.Wait, ordered[1].ActionType);
        Assert.Equal("500", ordered[1].Config.Value);
    }

    [Fact]
    public void 动作_add_step_拒绝非法动作类型()
    {
        var steps = Steps("s0");
        Assert.False(FixActionApplier.TryApply(steps, Action("add_step", null,
            new() { ["action_type"] = "HackThePlanet" }), out var err));
        Assert.NotNull(err);
    }

    // ------------------------------ ① 达标口径：Agent 自愈通过默认不计入 ------------------------------

    private static PlanRoundRaw Round(int total, int passed, int failed, int viaAgent) =>
        new(1, DateTime.UtcNow, DateTime.UtcNow, total, passed, failed, 0, 0, 0, 0,
            Array.Empty<PlanCaseOutcome>(), PassedViaAgent: viaAgent);

    [Fact]
    public void 达标判定_自愈通过默认不计入达标()
    {
        // 10 条中 9 通过，其中 1 条是 Agent 自愈 → 有效通过 8/10 = 0.8
        var round = Round(total: 10, passed: 9, failed: 1, viaAgent: 1);

        var (passed, rate, effFailed, _, _, _, reasons) =
            PlanGateEvaluator.Judge(round, 0.85, allowErrors: true, excludeFlaky: false,
                treatAgentHealedAsPass: false);
        Assert.Equal(0.8, rate, 3);
        Assert.False(passed);                        // 0.8 < 0.85 → 不达标
        Assert.Equal(2, effFailed);                  // 自愈通过被等量计入失败
        Assert.Contains(reasons, r => r.Contains("Agent 自愈"));

        // 设为计入后：9/10 = 0.9 → 达标
        var (passed2, rate2, _, _, _, _, _) =
            PlanGateEvaluator.Judge(round, 0.85, allowErrors: true, excludeFlaky: false,
                treatAgentHealedAsPass: true);
        Assert.Equal(0.9, rate2, 3);
        Assert.True(passed2);
    }

    [Fact]
    public void 达标判定_自愈说明不得把结论翻成不达标()
    {
        // 有效通过 9/10 = 0.9 ≥ 0.8 → 即便有 1 条自愈说明，也应判通过（说明只是告知）
        var (passed, rate, _, _, _, _, reasons) =
            PlanGateEvaluator.Judge(Round(total: 10, passed: 10, failed: 0, viaAgent: 1),
                0.8, allowErrors: true, excludeFlaky: false, treatAgentHealedAsPass: false);
        Assert.True(passed);
        Assert.Equal(0.9, rate, 3);
        Assert.Contains(reasons, r => r.Contains("Agent 自愈"));
    }

    // ------------------------------ 采纳路径：AppliedActions(JSON) → 应用到真实步骤 ------------------------------

    [Fact]
    public void 采纳路径_camelCase反序列化并应用到步骤()
    {
        // 与 AgentLoopService 的序列化口径一致（Web/camelCase）——用默认选项反序列化会绑定失败
        var json = """[{"actionType":"update_locator","stepOrder":1,"params":{"locator_type":"css","locator_value":"#new"},"confidence":0.9}]""";
        var fixes = JsonSerializer.Deserialize<List<FixActionDto>>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var steps = new List<TestStep> { Step(1, "#old") };
        var applied = fixes.Count(f => FixActionApplier.TryApply(steps, f, out _));

        Assert.Equal(1, applied);
        Assert.Equal("#new", steps[0].Config.Selector!.Value);
    }
}
