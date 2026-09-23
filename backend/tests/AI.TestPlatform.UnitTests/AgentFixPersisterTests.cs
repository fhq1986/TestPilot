using System.Text.Json;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// Agent「采纳」的动作应用逻辑（M8 审批闭环）。
///
/// 线上故障：点「采纳」后提示成功，用例步骤却毫无变化（Persisted=false）。
/// 两个原因叠在一起，这里各钉一条：
/// 1. 端点读错了字段——需审批的尝试在自愈循环应用动作**之前**就 break 了，
///    AppliedActions 对它必然为空，真正的动作在 ProposedFixes；
/// 2. FixActionApplier 的 add_step/delete_step 只改传入的 List，EF 追踪不到，
///    不按差集显式 Add/Remove 就等于没做。
///
/// 只测纯逻辑（<see cref="AgentFixPersister.Apply"/>）；最后那段 EF 落库由
/// AgentApprovalTests（集成测试）真库验证——TestDbContext 的 pgvector Vector 列
/// 无法用 InMemory provider 映射，在这里起不了内存库。
/// </summary>
public class AgentFixPersisterTests
{
    /// <summary>
    /// 真实 payload：从线上 AgentAttempts.ProposedFixes 抓的 4 个 add_step
    /// （失败用例「TC-PS-测试计划列表查询」的自愈建议）。不是照着自己的解析器编的——
    /// position 是字符串、Wait 用 timeout_ms 而非 value，这些"不整齐"的地方才是易错点。
    /// </summary>
    private const string RealProposedFixes = """
    [
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Fill","position":"0","value":"${username}","selector_type":"css","selector_value":"input[placeholder=\"用户名\"]","description":"登录页-用户名输入框"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Fill","position":"1","value":"${password}","selector_type":"css","selector_value":"input[placeholder=\"密码\"]","description":"登录页-密码输入框"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Click","position":"2","selector_type":"css","selector_value":"button.login-btn","description":"登录页-登录按钮"},"confidence":0.75},
      {"actionType":"add_step","stepOrder":0,"params":{"action_type":"Wait","position":"3","timeout_ms":"5000","description":"等待登录完成"},"confidence":0.6}
    ]
    """;

    private static List<FixActionDto> ParseFixes(string json) =>
        JsonSerializer.Deserialize<List<FixActionDto>>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>线上那条用例采纳前的三步：Navigate / Wait / AssertVisible</summary>
    private static List<TestStep> OriginalSteps() => new()
    {
        new TestStep { StepOrder = 1, ActionType = ActionType.Navigate,
            Config = new StepConfig { Url = "/test-plans" } },
        new TestStep { StepOrder = 2, ActionType = ActionType.Wait,
            Config = new StepConfig { Value = "3000" } },
        new TestStep { StepOrder = 3, ActionType = ActionType.AssertVisible,
            Config = new StepConfig { Selector = new SelectorConfig { Type = "css", Value = ".el-table" } } },
    };

    [Fact]
    public void 真实采纳payload_四个插入动作全部应用并按登录顺序就位()
    {
        var steps = OriginalSteps();

        var (applied, rejected, added, removed) =
            AgentFixPersister.Apply(steps, ParseFixes(RealProposedFixes), baseUrl: null);

        Assert.Equal(4, applied);
        Assert.Empty(rejected);
        Assert.Empty(removed);
        // 差集必须识别出 4 个新步骤——否则端点不会把它们 Add 进 DbSet，等于白采纳
        Assert.Equal(4, added.Count);

        Assert.Equal(7, steps.Count);
        Assert.Equal(
            new[]
            {
                ActionType.Fill, ActionType.Fill, ActionType.Click, ActionType.Wait,
                ActionType.Navigate, ActionType.Wait, ActionType.AssertVisible,
            },
            steps.OrderBy(s => s.StepOrder).Select(s => s.ActionType).ToArray());
    }

    [Fact]
    public void 落库顺序归一为1based与新建用例一致()
    {
        var steps = OriginalSteps();

        AgentFixPersister.Apply(steps, ParseFixes(RealProposedFixes), baseUrl: null);

        // applier 的 Renumber 是 0-based（执行期副本无所谓），落库会直接显示成详情页「序号」列，
        // 所以必须归一到 1-based，否则页面上会出现 0、1、2……的序号
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, steps.OrderBy(s => s.StepOrder).Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void 插入的Wait步骤拿到时长而不是空值()
    {
        var steps = OriginalSteps();

        AgentFixPersister.Apply(steps, ParseFixes(RealProposedFixes), baseUrl: null);

        // LLM 写的是 timeout_ms（wait_strategy 的参数名），TryAddStep 只认 value。
        // 不兜的话 Value 为 null，执行期直接抛「Wait 需要正整数毫秒数」——采纳即得到一条必挂的步骤
        var insertedWait = steps.Single(s => s.ActionType == ActionType.Wait && s.Config.Value == "5000");
        Assert.Equal("5000", insertedWait.Config.Value);
    }

    [Fact]
    public void 插入的Fill步骤带上定位符与取值()
    {
        var steps = OriginalSteps();

        AgentFixPersister.Apply(steps, ParseFixes(RealProposedFixes), baseUrl: null);

        var fills = steps.Where(s => s.ActionType == ActionType.Fill).OrderBy(s => s.StepOrder).ToList();
        Assert.Equal("${username}", fills[0].Config.Value);
        Assert.Equal("input[placeholder=\"用户名\"]", fills[0].Config.Selector!.Value);
        Assert.Equal("${password}", fills[1].Config.Value);
        Assert.Equal("input[placeholder=\"密码\"]", fills[1].Config.Selector!.Value);
    }

    [Fact]
    public void 删除动作进入Removed差集且顺序重排()
    {
        var steps = OriginalSteps();
        var fixes = ParseFixes("""
        [{"actionType":"delete_step","stepOrder":2,"params":{},"confidence":0.9}]
        """);

        var (applied, rejected, added, removed) = AgentFixPersister.Apply(steps, fixes, baseUrl: null);

        Assert.Equal(1, applied);
        Assert.Empty(rejected);
        Assert.Empty(added);
        // 只从 List 里摘掉的话 EF 仍视其为 Unchanged，不会 DELETE——必须靠差集显式 Remove
        Assert.Single(removed);
        Assert.Equal(ActionType.Wait, removed[0].ActionType);
        Assert.Equal(new[] { 1, 2 }, steps.OrderBy(s => s.StepOrder).Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void 越出被测站点的URL被拒并给出原因()
    {
        var steps = OriginalSteps();
        var fixes = ParseFixes("""
        [{"actionType":"add_step","stepOrder":1,"params":{"action_type":"Navigate","url":"https://evil.example.com/x"},"confidence":0.9}]
        """);

        var (applied, rejected, added, _) =
            AgentFixPersister.Apply(steps, fixes, baseUrl: "http://localhost:5210");

        Assert.Equal(0, applied);
        Assert.Empty(added);
        // 被拒原因必须回传：原来端点 out _ 吞掉原因，界面只能说「无可用动作可应用」
        var only = Assert.Single(rejected);
        Assert.Contains("越出", only.Reason);
    }

    [Fact]
    public void 不受支持的动作计入rejected且不改动步骤()
    {
        var steps = OriginalSteps();
        var fixes = ParseFixes("""
        [{"actionType":"drop_database","stepOrder":1,"params":{},"confidence":0.9}]
        """);

        var (applied, rejected, added, removed) = AgentFixPersister.Apply(steps, fixes, baseUrl: null);

        Assert.Equal(0, applied);
        Assert.Single(rejected);
        Assert.Equal("drop_database", rejected[0].ActionType);
        Assert.Empty(added);
        Assert.Empty(removed);
        // applied=0 时不能顺手重排——否则"什么都没采纳"却把用例步骤顺序改了
        Assert.Equal(new[] { 1, 2, 3 }, steps.Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void params为null的动作被拒而不是抛异常()
    {
        // LLM 输出是外部输入，"params": null 完全可能出现；
        // 不拦的话 TryGetString 会 NRE，采纳端点直接 500
        var steps = OriginalSteps();
        var fixes = ParseFixes("""
        [{"actionType":"update_locator","stepOrder":1,"params":null,"confidence":0.9}]
        """);

        var (applied, rejected, _, _) = AgentFixPersister.Apply(steps, fixes, baseUrl: null);

        Assert.Equal(0, applied);
        Assert.Contains("缺少 params", Assert.Single(rejected).Reason);
    }

    [Fact]
    public void 部分被拒时其余动作照常应用()
    {
        var steps = OriginalSteps();
        var fixes = ParseFixes("""
        [
          {"actionType":"add_step","stepOrder":1,"params":{"action_type":"Navigate","url":"https://evil.example.com/x"},"confidence":0.9},
          {"actionType":"update_locator","stepOrder":3,"params":{"locator_type":"css","locator_value":".data-table"},"confidence":0.9}
        ]
        """);

        var (applied, rejected, _, _) =
            AgentFixPersister.Apply(steps, fixes, baseUrl: "http://localhost:5210");

        Assert.Equal(1, applied);
        Assert.Single(rejected);
        Assert.Equal(".data-table", steps.Single(s => s.StepOrder == 3).Config.Selector!.Value);
    }
}
