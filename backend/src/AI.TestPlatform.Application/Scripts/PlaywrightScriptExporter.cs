using System.Text;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Scripts;

/// <summary>
/// Playwright 脚本导出：把平台的用例反写成一份可直接入 Git、可 review 的 Playwright 脚本。
///
/// 与解析器互为反向通道：
/// - css/xpath 选择器 → <c>page.locator('…')</c>
/// - ai 描述选择器 → <c>page.getByText('…')</c> + 注释（保留原始 AI 描述，便于人工改精确）
/// - API 用例 → <c>request.fetch</c> 形式
/// </summary>
public static class PlaywrightScriptExporter
{
    public static string Export(TestCase testCase, IReadOnlyList<TestStep>? steps = null, string? baseUrl = null)
    {
        var list = (steps ?? testCase.Steps).OrderBy(s => s.StepOrder).ToList();
        var isApi = testCase.Type == TestType.Api;
        var effectiveBaseUrl = baseUrl ?? testCase.BaseUrl;

        var builder = new StringBuilder();
        builder.AppendLine("// 由 AI 自动化测试平台导出，可直接放入 Git 并在 Playwright 中运行");
        builder.AppendLine($"// 用例：{testCase.Name}" +
                           (string.IsNullOrWhiteSpace(testCase.CaseCode) ? string.Empty : $"（{testCase.CaseCode}）"));
        if (!string.IsNullOrWhiteSpace(testCase.Description))
            builder.AppendLine($"// 说明：{testCase.Description}");
        if (!string.IsNullOrWhiteSpace(testCase.Module))
            builder.AppendLine($"// 模块：{testCase.Module}");
        if (!string.IsNullOrWhiteSpace(testCase.Priority))
            builder.AppendLine($"// 优先级：{testCase.Priority}");
        if (!string.IsNullOrWhiteSpace(effectiveBaseUrl))
            builder.AppendLine($"// 基地址：{effectiveBaseUrl}");
        builder.AppendLine();

        if (isApi)
        {
            builder.AppendLine("import { test, expect } from '@playwright/test';");
            builder.AppendLine();
            builder.AppendLine($"test('{EscapeSingle(testCase.Name)}', async ({{ request }}) => {{");
        }
        else
        {
            builder.AppendLine("import { test, expect } from '@playwright/test';");
            builder.AppendLine();
            builder.AppendLine($"test('{EscapeSingle(testCase.Name)}', async ({{ page }}) => {{");
        }

        foreach (var step in list)
        {
            var line = ExportStep(step, isApi, effectiveBaseUrl);
            if (line is null) continue;
            builder.AppendLine($"  {line}");
        }

        builder.AppendLine("});");
        return builder.ToString();
    }

    private static string? ExportStep(TestStep step, bool isApi, string? baseUrl)
    {
        var config = step.Config ?? new StepConfig();
        var comment = string.IsNullOrWhiteSpace(step.AIElementDescription)
            ? string.Empty
            : $" // 元素描述：{step.AIElementDescription}";

        if (isApi) return ExportApiStep(step.ActionType, config);

        switch (step.ActionType)
        {
            case ActionType.Navigate:
                return config.Url is null ? null : $"await page.goto('{EscapeSingle(config.Url)}');";

            case ActionType.Fill:
                return $"await {Locator(config.Selector)}.fill('{EscapeSingle(config.Value ?? string.Empty)}');{comment}";

            case ActionType.Click:
                return $"await {Locator(config.Selector)}.click();{comment}";

            case ActionType.Wait:
                return config.Selector is null
                    ? $"await page.waitForTimeout({Number(config.Value)});"
                    : $"await {Locator(config.Selector)}.waitFor({{ state: 'visible' }});{comment}";

            case ActionType.Screenshot:
                return $"await page.screenshot({{ path: 'step-{step.StepOrder + 1}.png', fullPage: true }});";

            case ActionType.Scroll:
                return config.Selector is null
                    ? "await page.mouse.wheel(0, document.body?.scrollHeight ?? 800);"
                    : $"await {Locator(config.Selector)}.scrollIntoViewIfNeeded();{comment}";

            case ActionType.AssertVisible:
                return $"await expect({Locator(config.Selector)}).toBeVisible();{comment}";

            case ActionType.AssertText:
                return $"await expect({Locator(config.Selector)}).toContainText('{EscapeSingle(config.Value ?? string.Empty)}');{comment}";

            case ActionType.AssertUrl:
                return $"await expect(page).toHaveURL(/{EscapeRegex(config.Value ?? string.Empty)}/);";

            case ActionType.AssertTitle:
                return $"await expect(page).toHaveTitle(/{EscapeRegex(config.Value ?? string.Empty)}/);";

            case ActionType.AssertA11y:
                // 导出为可运行脚本时给出等价写法：自己注入 axe 再断言。
                // 刻意不导出成注释——这条断言在 CI 里也应该真的跑起来。
                // 用纯 JS 写法（无类型断言），另存为 .js 也能直接运行。
                //
                // 这里用字符串拼接而不是原始字符串字面量：内插原始字符串里的 {{ }} 会与
                // JS 对象字面量的花括号冲突（CS9006），拼接反而更清楚。
                var level = string.IsNullOrWhiteSpace(config.Value) ? "serious" : config.Value!;
                var lines = new[]
                {
                    $"// 无障碍扫描：拦截 {level} 及以上",
                    "await page.addScriptTag({ path: 'axe.min.js' });",
                    "const a11yViolations = await page.evaluate(async () => {",
                    "  const result = await window.axe.run(document, { resultTypes: ['violations'] });",
                    "  return result.violations.map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.length }));",
                    "});",
                    $"const a11yBlocking = a11yViolations.filter(v => ['{level}', 'critical'].includes(v.impact));",
                    "expect(a11yBlocking, JSON.stringify(a11yBlocking)).toHaveLength(0);",
                };
                // 调用方只给第一行加了缩进，后续行要自带 —— 否则脚本缩进会乱
                return string.Join("\n  ", lines);

            case ActionType.AIAssert:
                return $"// AI 断言（平台能力）：{EscapeSingle(step.AIInstruction ?? config.Value ?? string.Empty)}";

            case ActionType.AIAction:
                return $"// AI 动作（平台能力）：{EscapeSingle(step.AIInstruction ?? string.Empty)}";

            default:
                return $"// 未支持的动作 {step.ActionType}，请人工补充";
        }
    }

    private static string? ExportApiStep(ActionType actionType, StepConfig config)
    {
        switch (actionType)
        {
            case ActionType.Request:
            {
                var method = string.IsNullOrWhiteSpace(config.Method) ? "GET" : config.Method!.ToUpperInvariant();
                var headers = config.Headers is { Count: > 0 }
                    ? $", headers: {{ {string.Join(", ", config.Headers.Select(h => $"'{EscapeSingle(h.Name)}': '{EscapeSingle(h.Value)}'"))} }}"
                    : string.Empty;
                var data = string.IsNullOrWhiteSpace(config.Body)
                    ? string.Empty
                    : $", data: {config.Body}";
                return $"const response = await request.fetch('{EscapeSingle(config.Endpoint ?? string.Empty)}', " +
                       $"{{ method: '{method}'{headers}{data} }});";
            }
            case ActionType.AssertResponse:
                return "expect(response.ok()).toBeTruthy();";
            case ActionType.ExtractVariable:
                return "// 变量提取：平台在执行时保存响应字段，脚本里用 const body = await response.json(); 自行处理";
            default:
                return $"// 未支持的 API 动作 {actionType}";
        }
    }

    /// <summary>定位器输出：css/xpath 直接输出，ai 描述退化成语义定位 + 注释保留原描述</summary>
    public static string Locator(SelectorConfig? selector)
    {
        if (selector is null || string.IsNullOrWhiteSpace(selector.Value) && string.IsNullOrWhiteSpace(selector.Description))
            return "page.locator('body') /* 未配置选择器，请人工确认 */";

        var type = (selector.Type ?? "css").ToLowerInvariant();
        if (type == "xpath")
            return $"page.locator('xpath={EscapeSingle(selector.Value ?? string.Empty)}')";
        if (type == "ai")
        {
            var description = selector.Description ?? selector.Value ?? string.Empty;
            return $"page.getByText('{EscapeSingle(description)}') /* AI 定位：{EscapeSingle(description)} */";
        }
        return $"page.locator('{EscapeSingle(selector.Value ?? string.Empty)}')";
    }

    private static string EscapeSingle(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", string.Empty).Replace("\n", "\\n");

    private static string EscapeRegex(string value) =>
        value.Replace("\\", "\\\\").Replace("/", "\\/").Replace("\r", string.Empty).Replace("\n", "\\n");

    private static string Number(string? value) =>
        int.TryParse(value, out var ms) ? ms.ToString() : "1000";
}
