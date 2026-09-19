using AI.TestPlatform.Application.SharedSteps;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 共享步骤展开。这是「改一处全用例生效」的核心：
/// 展开必须发生在运行时，且变量优先级、序号重排、失效引用都不能出错。
/// </summary>
public class SharedStepExpanderTests
{
    private static SharedStepGroup Group(params SharedStepItem[] items) => new()
    {
        Id = Guid.NewGuid(),
        Name = "登录",
        Items = items.ToList(),
    };

    private static SharedStepItem Item(int order, ActionType action, StepConfig config) => new()
    {
        StepOrder = order,
        ActionType = action,
        Config = config,
    };

    private static TestStep Plain(int order, ActionType action, string? url = null) => new()
    {
        StepOrder = order,
        ActionType = action,
        Config = new StepConfig { Url = url },
    };

    private static TestStep Ref(int order, SharedStepGroup group,
        List<SharedVariableEntry>? overrides = null) => new()
    {
        StepOrder = order,
        ActionType = ActionType.AIAction,
        Config = new StepConfig(),
        SharedGroupId = group.Id,
        SharedVariables = overrides,
    };

    [Fact]
    public void 没有共享引用时原样保留并重排序号()
    {
        var steps = new[]
        {
            Plain(3, ActionType.Click),
            Plain(1, ActionType.Navigate, "https://a.test"),
        };

        var expanded = SharedStepExpander.Expand(steps, new Dictionary<Guid, SharedStepGroup>());

        Assert.Equal(2, expanded.Count);
        // 输入顺序是 3,1 → 输出必须按 StepOrder 排好并重新编号为 0,1
        Assert.Equal(0, expanded[0].StepOrder);
        Assert.Equal(ActionType.Navigate, expanded[0].ActionType);
        Assert.Equal(1, expanded[1].StepOrder);
    }

    [Fact]
    public void 共享步骤组被展开且序号连续()
    {
        var group = Group(
            Item(0, ActionType.Navigate, new StepConfig { Url = "https://login.test" }),
            Item(1, ActionType.Fill, new StepConfig { Value = "admin" }),
            Item(2, ActionType.Click, new StepConfig()));

        var steps = new[]
        {
            Ref(0, group),
            Plain(1, ActionType.AssertVisible),
        };

        var expanded = SharedStepExpander.Expand(
            steps, new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        // 3 个组内步骤 + 1 个普通步骤
        Assert.Equal(4, expanded.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, expanded.Select(s => s.StepOrder).ToArray());
        Assert.Equal("https://login.test", expanded[0].Config.Url);
        Assert.Equal("admin", expanded[1].Config.Value);
        Assert.Equal(ActionType.AssertVisible, expanded[3].ActionType);
        // 展开后的步骤不再是引用（否则会无限递归展开）
        Assert.All(expanded, s => Assert.Null(s.SharedGroupId));
    }

    [Fact]
    public void 共享步骤组出现在用例中间时后续步骤顺延()
    {
        var group = Group(Item(0, ActionType.Click, new StepConfig()));

        var steps = new[]
        {
            Plain(0, ActionType.Navigate),
            Ref(1, group),
            Plain(2, ActionType.AssertUrl),
        };

        var expanded = SharedStepExpander.Expand(
            steps, new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        Assert.Equal(3, expanded.Count);
        Assert.Equal(ActionType.Navigate, expanded[0].ActionType);
        Assert.Equal(ActionType.Click, expanded[1].ActionType);
        Assert.Equal(ActionType.AssertUrl, expanded[2].ActionType);
        Assert.Equal(new[] { 0, 1, 2 }, expanded.Select(s => s.StepOrder).ToArray());
    }

    [Fact]
    public void 组内默认变量在展开时被替换()
    {
        var group = Group(Item(0, ActionType.Fill, new StepConfig { Value = "{{username}}" }));
        group.Variables = new List<SharedVariableEntry>
        {
            new() { Name = "username", Value = "default-user" },
        };

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        Assert.Equal("default-user", expanded[0].Config!.Value);
    }

    [Fact]
    public void 引用方覆盖优先于组内默认值()
    {
        var group = Group(Item(0, ActionType.Fill, new StepConfig { Value = "{{username}}" }));
        group.Variables = new List<SharedVariableEntry>
        {
            new() { Name = "username", Value = "default-user" },
        };

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group, new List<SharedVariableEntry> { new() { Name = "username", Value = "case-user" } }) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        Assert.Equal("case-user", expanded[0].Config!.Value);
    }

    [Fact]
    public void 变量名匹配大小写不敏感()
    {
        var group = Group(Item(0, ActionType.Fill, new StepConfig { Value = "{{UserName}}" }));
        group.Variables = new List<SharedVariableEntry>
        {
            new() { Name = "username", Value = "u1" },
        };

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        Assert.Equal("u1", expanded[0].Config!.Value);
    }

    [Fact]
    public void 未命中的占位符原样保留而不是变成空串()
    {
        var group = Group(Item(0, ActionType.Fill, new StepConfig { Value = "{{missing}}" }));

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        // 静默变成空串会让拼写错误极难排查
        Assert.Equal("{{missing}}", expanded[0].Config!.Value);
    }

    [Fact]
    public void 组被删除时跳过引用并记录告警()
    {
        var warnings = new List<string>();
        var steps = new[] { Ref(0, Group(Item(0, ActionType.Click, new StepConfig()))) };

        var expanded = SharedStepExpander.Expand(steps, new Dictionary<Guid, SharedStepGroup>(), warnings);

        Assert.Empty(expanded);
        Assert.Single(warnings);
        Assert.Contains("已不存在", warnings[0]);
    }

    [Fact]
    public void 组内没有步骤时跳过并记录告警()
    {
        var group = Group();
        var warnings = new List<string>();

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group }, warnings);

        Assert.Empty(expanded);
        Assert.Single(warnings);
        Assert.Contains("没有任何步骤", warnings[0]);
    }

    [Fact]
    public void 同一个组被引用多次时各自独立展开()
    {
        var group = Group(Item(0, ActionType.Click, new StepConfig()));

        var expanded = SharedStepExpander.Expand(
            new[] { Ref(0, group), Ref(1, group) },
            new Dictionary<Guid, SharedStepGroup> { [group.Id] = group });

        Assert.Equal(2, expanded.Count);
        Assert.Equal(new[] { 0, 1 }, expanded.Select(s => s.StepOrder).ToArray());
        // 必须是不同实例：同一个 TestStep 被塞两次会让下游改序号时互相污染
        Assert.NotSame(expanded[0], expanded[1]);
    }

    [Fact]
    public void 引用清单只返回去重后的组ID()
    {
        var a = Group(Item(0, ActionType.Click, new StepConfig()));
        var b = Group(Item(0, ActionType.Click, new StepConfig()));

        var ids = SharedStepExpander.ReferencedGroupIds(new[] { Ref(0, a), Ref(1, a), Ref(2, b), Plain(3, ActionType.Click) });

        Assert.Equal(2, ids.Count);
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
    }

    [Fact]
    public void 普通步骤的配置对象不被复制走样()
    {
        var original = Plain(0, ActionType.Navigate, "https://keep.test");

        var expanded = SharedStepExpander.Expand(new[] { original }, new Dictionary<Guid, SharedStepGroup>());

        Assert.Equal("https://keep.test", expanded[0].Config.Url);
        // 无占位符时 StepVariableResolver 走的是直接返回原对象的分支，Config 应保持同一引用
        Assert.Same(original.Config, expanded[0].Config);
    }
}
