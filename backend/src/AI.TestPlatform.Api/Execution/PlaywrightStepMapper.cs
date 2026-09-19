using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Execution;

public static class PlaywrightStepMapper
{
    public static string ToLocator(SelectorConfig? selector)
    {
        if (selector is null)
            throw new StepExecutionException("缺少元素选择器");
        if (string.Equals(selector.Type, "ai", StringComparison.OrdinalIgnoreCase))
            throw new StepExecutionException("AI 元素定位将在 M4 支持");
        if (string.IsNullOrWhiteSpace(selector.Value))
            throw new StepExecutionException("选择器缺少 Value");
        return selector.Type switch
        {
            "css" => selector.Value,
            "xpath" => selector.Value,
            _ => throw new StepExecutionException($"不支持的选择器类型: {selector.Type}"),
        };
    }

    public static int ParseWaitMs(string? value)
    {
        if (!int.TryParse(value, out var ms) || ms <= 0)
            throw new StepExecutionException($"Wait 需要正整数毫秒数(Value)，实际: {value}");
        return ms;
    }
}
