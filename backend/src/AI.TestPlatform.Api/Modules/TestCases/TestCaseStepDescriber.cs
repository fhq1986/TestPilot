using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>
/// 把用例的可执行步骤转换成报告可读文本：
/// 「操作步骤」优先沿用导入原文，缺失时按步骤生成；「预期结果」优先导入原文，缺失时由断言步骤归纳。
/// </summary>
public static class TestCaseStepDescriber
{
    /// <summary>操作步骤：优先用导入时保留的原文，否则按用例的实际步骤生成可读描述。</summary>
    public static string ResolveSourceSteps(TestCase? testCase)
    {
        if (testCase is null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(testCase.SourceSteps))
            return testCase.SourceSteps!;
        return DescribeSteps(testCase.Steps);
    }

    /// <summary>预期结果：优先用导入原文，否则从断言类步骤归纳。</summary>
    public static string ResolveExpectedResult(TestCase? testCase)
    {
        if (testCase is null)
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(testCase.ExpectedResult))
            return testCase.ExpectedResult!;
        return DescribeExpectation(testCase.Steps);
    }

    /// <summary>把可执行步骤拼成操作步骤文本（供报告展示）。</summary>
    public static string DescribeSteps(IEnumerable<TestStep> steps)
    {
        var lines = new List<string>();
        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            var cfg = step.Config ?? new StepConfig();
            var target = DescribeTarget(cfg);
            var text = step.ActionType switch
            {
                ActionType.Navigate => $"打开 {cfg.Url ?? "/"}",
                ActionType.Fill => $"在{target}输入「{cfg.Value}」",
                ActionType.Click => $"点击{target}",
                ActionType.Wait => string.IsNullOrWhiteSpace(cfg.Value) ? $"等待{target}出现" : $"等待 {cfg.Value} 毫秒",
                ActionType.Screenshot => "截图",
                ActionType.Scroll => string.IsNullOrWhiteSpace(cfg.Selector?.Value)
                    ? "滚动到页面底部"
                    : $"滚动到{target}",
                ActionType.AssertVisible => $"断言{target}可见",
                ActionType.AssertText => $"断言{target}文本包含「{cfg.Value}」",
                ActionType.AssertUrl => $"断言页面地址包含「{cfg.Value}」",
                ActionType.AssertTitle => $"断言页面标题包含「{cfg.Value}」",
                ActionType.AssertA11y => string.IsNullOrWhiteSpace(cfg.Value)
                    ? "无障碍扫描（拦截严重及以上）"
                    : $"无障碍扫描（拦截「{cfg.Value}」及以上）",
                ActionType.AIAssert => $"AI 断言：{cfg.Value ?? step.AIInstruction}",
                ActionType.AIAction => $"AI 操作：{step.AIInstruction ?? cfg.Value}",
                ActionType.Request => $"请求 {cfg.Method} {cfg.Endpoint}",
                ActionType.AssertResponse => $"断言接口响应「{cfg.Value}」",
                ActionType.ExtractVariable => $"提取变量「{cfg.Value}」",
                _ => step.ActionType.ToString(),
            };
            if (!string.IsNullOrWhiteSpace(step.AIElementDescription) &&
                text.Contains("目标元素", StringComparison.Ordinal))
                text = text.Replace("目标元素", $"「{step.AIElementDescription}」");
            lines.Add($"{lines.Count + 1}. {text}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>从断言类步骤归纳预期结果。</summary>
    public static string DescribeExpectation(IEnumerable<TestStep> steps)
    {
        var items = new List<string>();
        foreach (var step in steps.OrderBy(s => s.StepOrder))
        {
            var cfg = step.Config ?? new StepConfig();
            var target = DescribeTarget(cfg);
            switch (step.ActionType)
            {
                case ActionType.AssertText:
                    items.Add($"{target}显示「{cfg.Value}」");
                    break;
                case ActionType.AssertVisible:
                    items.Add($"{target}可见");
                    break;
                case ActionType.AssertUrl:
                    items.Add($"页面地址包含「{cfg.Value}」");
                    break;
                case ActionType.AssertTitle:
                    items.Add($"页面标题包含「{cfg.Value}」");
                    break;
                case ActionType.AssertA11y:
                    items.Add(string.IsNullOrWhiteSpace(cfg.Value)
                        ? "无严重及以上无障碍违规"
                        : $"无「{cfg.Value}」及以上无障碍违规");
                    break;
                case ActionType.AssertResponse:
                    items.Add($"接口响应符合「{cfg.Value}」");
                    break;
                case ActionType.AIAssert:
                    items.Add($"满足：{cfg.Value ?? step.AIInstruction}");
                    break;
            }
        }
        return string.Join("；", items);
    }

    /// <summary>步骤目标的可读描述：优先元素描述，其次选择器，最后「目标元素」。</summary>
    public static string DescribeTarget(StepConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Selector?.Description))
            return $"「{cfg.Selector!.Description}」";
        if (!string.IsNullOrWhiteSpace(cfg.Selector?.Value))
            return $"「{cfg.Selector!.Value}」";
        return "目标元素";
    }
}
