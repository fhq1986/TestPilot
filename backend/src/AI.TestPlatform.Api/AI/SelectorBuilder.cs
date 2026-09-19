using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.AI;

// 把 LLM 选中的元素确定性转为稳定 selector（避免 LLM 幻觉）
public static class SelectorBuilder
{
    public static SelectorConfig? Build(InteractiveElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Id))
            return new SelectorConfig { Type = "css", Value = $"[id=\"{Escape(element.Id)}\"]" };
        if (!string.IsNullOrWhiteSpace(element.Aria))
            return new SelectorConfig { Type = "css", Value = $"[aria-label=\"{Escape(element.Aria)}\"]" };
        var text = (element.Text ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(text) && text.Length <= 40)
        {
            var tag = string.IsNullOrWhiteSpace(element.Tag) ? "*" : element.Tag.ToLowerInvariant();
            var textExpr = text.Contains('"')
                ? $"concat({string.Join(", '\"', ", text.Split('"').Select(p => $"\"{p}\""))})"
                : $"\"{text}\"";
            return new SelectorConfig
            {
                Type = "xpath",
                Value = $"(//{tag}[normalize-space()={textExpr}])[1]",
            };
        }
        return null;
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
