using System.Text.Json;
using System.Text.Json.Serialization;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.TestCases;

/// <summary>
/// 用例内容快照。**只包含"内容"**——名称、步骤、断言这些改了就换版本的东西。
///
/// 刻意**不含** UpdatedAt / Status / IsFlaky / FlakeRate / DeletedAt：
/// 那些是执行和治理过程自动写的（比如执行通过会把状态提升为启用），
/// 混进来的话"什么都没改但状态被后台改了"也会多出一个版本，历史很快被噪声淹没。
/// </summary>
public record TestCaseSnapshot(
    string Name,
    string? Description,
    TestType Type,
    string? CaseCode,
    string? Module,
    string? Priority,
    string? Browser,
    int Timeout,
    int RetryCount,
    bool FailFast,
    string? BaseUrl,
    string? ExpectedResult,
    string? SourceSteps,
    bool VisualEnabled,
    double VisualThreshold,
    string? VisualIgnoreRegions,
    string? CustomFields,
    CaseReviewStatus ReviewStatus,
    Guid? DataSetId,
    Guid? RequirementId,
    IReadOnlyList<TestStepSnapshot> Steps,
    /// <summary>
    /// 用例级网络规则（JSON 原文）。
    ///
    /// **必须进快照**：它是用例的执行语义之一，规则改了却不记版本的话，
    /// "上次跑通、这次跑挂"就查不出是因为谁改了拦截规则——版本历史在这里就失效了。
    /// 放在末尾并带默认值，是为了让升级前存下的快照仍能反序列化。
    /// </summary>
    string? NetworkRules = null);

/// <summary>
/// 步骤快照。<see cref="Config"/> 直接存序列化后的 JSON 字符串，快照因此自成一体、不依赖别表。
///
/// **刻意不存共享步骤组的名字**：那是能从 <see cref="SharedGroupId"/> 推导出来的展示字段，
/// 而"从请求构造新快照"那边拿不到名字（请求里只有 id）——放进来就会变成
/// "旧快照有名字、新快照没有"，签名永远对不上，于是**每次保存都凭空多一版**。
/// 展示用的名字由前端按 id 去映射。
/// </summary>
public record TestStepSnapshot(
    int StepOrder,
    ActionType ActionType,
    string Config,
    string? AIInstruction,
    string? AIElementDescription,
    Guid? SharedGroupId,
    IReadOnlyList<SharedVariableEntry>? SharedVariables);

public static class TestCaseSnapshotExtensions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // **必须显式用 camelCase**：整个 API 的 JSON 都是 camelCase，
        // 而这里默认会用 C# 属性名（PascalCase）——快照里就成了 `{"Url":...}`，
        // 接口返回的用例却是 `{"url":...}`。前端做版本对比时两边永远不相等，
        // **每一项都被判成"变了"**（实测把 14 个步骤全部误报成有差异）。
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 同时容忍老数据（之前存的是 PascalCase）：快照是要长期留存的，
        // 不能因为改了命名策略就读不出来
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>把用例的当前内容抽成快照（步骤按顺序，保证内容相同则快照稳定）</summary>
    public static TestCaseSnapshot ToSnapshot(this TestCase tc) => new(
        tc.Name, tc.Description, tc.Type, tc.CaseCode, tc.Module, tc.Priority,
        tc.Browser, tc.Timeout, tc.RetryCount, tc.FailFast, tc.BaseUrl,
        tc.ExpectedResult, tc.SourceSteps, tc.VisualEnabled, tc.VisualThreshold, tc.VisualIgnoreRegions, tc.CustomFields, tc.ReviewStatus,
        tc.DataSetId, tc.RequirementId,
        tc.Steps.OrderBy(s => s.StepOrder).Select(s => ToStepSnapshot(
            s.StepOrder, s.ActionType, s.Config, s.AIInstruction, s.AIElementDescription,
            s.SharedGroupId, s.SharedVariables)).ToList(),
        tc.NetworkRules);

    /// <summary>
    /// 从"即将写入的数据"构造步骤快照。
    ///
    /// 端点里构造 incoming 快照**必须走这里**，不要自己 `JsonSerializer.Serialize(Config)`：
    /// 序列化选项只要和 <see cref="ToSnapshot"/> 有一点不一致，内容签名就永远对不上，
    /// 结果是**每次保存都凭空多出一版**——版本历史会被噪声淹没，而且很难查出原因。
    /// </summary>
    public static TestStepSnapshot ToStepSnapshot(
        int stepOrder, ActionType actionType, StepConfig config,
        string? aiInstruction, string? aiElementDescription,
        Guid? sharedGroupId,
        IReadOnlyList<SharedVariableEntry>? sharedVariables) =>
        new(stepOrder, actionType, JsonSerializer.Serialize(config, Options),
            aiInstruction, aiElementDescription, sharedGroupId, sharedVariables);

    /// <summary>内容签名——只用来判断"这次保存到底改没改内容"，不落库</summary>
    public static string Signature(this TestCaseSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static string Serialize(this TestCaseSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static TestCaseSnapshot? Deserialize(string json)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<TestCaseSnapshot>(json, Options);
            // **在边界上把步骤配置规范一遍**：老快照里的 config 可能是另一种命名策略
            // （本项目就经历过 PascalCase → camelCase），而下游的"内容变没变"签名、
            // "改了什么"摘要都是按字符串比的——不在这里统一，就会把内容相同的步骤
            // 全判成"改过"（实测摘要里出现过"第 1~14 步内容有改动"这种误报）。
            return snapshot is null ? null : Normalize(snapshot);
        }
        catch (JsonException)
        {
            // 快照格式变了（比如加过字段）就当作读不出来，不抛给调用方——历史不该让详情页报错
            return null;
        }
    }

    /// <summary>把每个步骤的 config 重新按当前序列化选项写一遍，消除历史数据的格式差异</summary>
    private static TestCaseSnapshot Normalize(TestCaseSnapshot snapshot) => snapshot with
    {
        Steps = snapshot.Steps.Select(s => s with
        {
            Config = CanonicalConfig(s.Config),
        }).ToList(),
    };

    private static string CanonicalConfig(string raw)
    {
        var parsed = ParseConfig(raw);
        return parsed is null ? raw : JsonSerializer.Serialize(parsed, Options);
    }

    /// <summary>
    /// 解析快照里的步骤配置。
    ///
    /// **必须用这里的实现，不要在别处 `JsonSerializer.Deserialize&lt;StepConfig&gt;(json)`**：
    /// 默认选项区分大小写，读旧格式（PascalCase）会**绑定不上却不报错**，
    /// 结果是步骤配置静默变成一堆 null —— 回滚一次就把用例的步骤内容清空了（真踩过）。
    /// </summary>
    public static StepConfig? ParseConfig(string raw)
    {
        try
        {
            return JsonSerializer.Deserialize<StepConfig>(raw, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 生成"人话"变更摘要，供版本列表直接展示。
    /// 只列**真正变了**的项，没变的不提——否则每条历史都是同样一长串，等于没写。
    /// </summary>
    public static string BuildSummary(TestCaseSnapshot old, TestCaseSnapshot now)
    {
        var parts = new List<string>();

        if (old.Name != now.Name) parts.Add($"名称「{old.Name}」→「{now.Name}」");
        if (old.Description != now.Description) parts.Add("描述已改");
        if (old.CaseCode != now.CaseCode) parts.Add($"编号 {Text(old.CaseCode)} → {Text(now.CaseCode)}");
        if (old.Module != now.Module) parts.Add($"模块 {Text(old.Module)} → {Text(now.Module)}");
        if (old.Priority != now.Priority) parts.Add($"优先级 {Text(old.Priority)} → {Text(now.Priority)}");
        if (old.Type != now.Type) parts.Add($"类型 {old.Type} → {now.Type}");
        if (old.Browser != now.Browser) parts.Add($"浏览器 {Text(old.Browser)} → {Text(now.Browser)}");
        if (old.Timeout != now.Timeout) parts.Add($"超时 {old.Timeout} → {now.Timeout}");
        if (old.RetryCount != now.RetryCount) parts.Add($"重试 {old.RetryCount} → {now.RetryCount}");
        if (old.FailFast != now.FailFast) parts.Add($"失败即停 {OnOff(old.FailFast)} → {OnOff(now.FailFast)}");
        if (old.BaseUrl != now.BaseUrl) parts.Add("基址已改");
        if (old.ExpectedResult != now.ExpectedResult) parts.Add("预期结果已改");
        if (old.VisualEnabled != now.VisualEnabled) parts.Add($"视觉回归 {OnOff(old.VisualEnabled)} → {OnOff(now.VisualEnabled)}");
        if (old.VisualThreshold != now.VisualThreshold) parts.Add($"视觉阈值 {old.VisualThreshold:P2} → {now.VisualThreshold:P2}");
        if (old.VisualIgnoreRegions != now.VisualIgnoreRegions) parts.Add("视觉忽略区域已调整");
        if (old.CustomFields != now.CustomFields) parts.Add("扩展字段已调整");
        if (old.ReviewStatus != now.ReviewStatus) parts.Add($"评审状态 {old.ReviewStatus} → {now.ReviewStatus}");
        if (old.DataSetId != now.DataSetId) parts.Add("数据集绑定已改");
        if (old.RequirementId != now.RequirementId) parts.Add("关联需求已改");

        if (old.Steps.Count != now.Steps.Count)
        {
            parts.Add($"步骤 {old.Steps.Count} → {now.Steps.Count} 步");
        }
        else
        {
            var changed = ChangedSteps(old.Steps, now.Steps);
            if (changed.Count > 0)
                parts.Add($"第 {string.Join("、", changed)} 步内容有改动");
        }

        return parts.Count == 0 ? "无内容变化" : string.Join("；", parts);
    }

    /// <summary>步骤号相同的那些里，内容不一致的步骤号（步数变化时由调用方单独报）</summary>
    private static List<int> ChangedSteps(
        IReadOnlyList<TestStepSnapshot> old, IReadOnlyList<TestStepSnapshot> now)
    {
        var olds = old.ToDictionary(s => s.StepOrder);
        return now.Where(s => olds.TryGetValue(s.StepOrder, out var o) && !SameStep(o, s))
            .Select(s => s.StepOrder + 1)   // 展示用 1-based
            .ToList();
    }

    private static bool SameStep(TestStepSnapshot a, TestStepSnapshot b) =>
        a.ActionType == b.ActionType && a.Config == b.Config &&
        a.AIInstruction == b.AIInstruction && a.AIElementDescription == b.AIElementDescription &&
        a.SharedGroupId == b.SharedGroupId;

    private static string Text(string? v) => string.IsNullOrWhiteSpace(v) ? "(空)" : v;
    private static string OnOff(bool v) => v ? "开" : "关";
}
