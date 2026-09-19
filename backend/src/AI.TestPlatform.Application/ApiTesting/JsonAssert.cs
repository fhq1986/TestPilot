using System.Text.Json;
using System.Text.RegularExpressions;

namespace AI.TestPlatform.Application.ApiTesting;

public static class JsonAssert
{
    // 子集匹配：期望 JSON 的每个键/值都在实际 JSON 中存在且相等（对象递归、数组按元素包含）
    public static bool IsSubset(JsonElement expected, JsonElement actual, out string? mismatch)
    {
        mismatch = null;
        return IsSubsetCore(expected, actual, "$", ref mismatch);
    }

    private static bool IsSubsetCore(JsonElement expected, JsonElement actual, string path, ref string? mismatch)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            mismatch = $"{path}: 类型不匹配";
            return false;
        }
        return expected.ValueKind switch
        {
            JsonValueKind.Object => IsObjectSubset(expected, actual, path, ref mismatch),
            JsonValueKind.Array => IsArraySubset(expected, actual, path, ref mismatch),
            _ => IsScalarEqual(expected, actual, path, ref mismatch),
        };
    }

    private static bool IsObjectSubset(JsonElement expected, JsonElement actual, string path, ref string? mismatch)
    {
        foreach (var prop in expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(prop.Name, out var actualValue))
            {
                mismatch = $"{path}.{prop.Name}: 缺失";
                return false;
            }
            if (!IsSubsetCore(prop.Value, actualValue, $"{path}.{prop.Name}", ref mismatch))
                return false;
        }
        return true;
    }

    private static bool IsArraySubset(JsonElement expected, JsonElement actual, string path, ref string? mismatch)
    {
        var actualItems = actual.EnumerateArray().ToList();
        foreach (var expectedItem in expected.EnumerateArray())
        {
            var anyMatch = actualItems.Any(a =>
            {
                string? inner = null;
                return IsSubsetCore(expectedItem, a, path, ref inner);
            });
            if (!anyMatch)
            {
                mismatch = $"{path}[?]: 无匹配元素";
                return false;
            }
        }
        return true;
    }

    private static bool IsScalarEqual(JsonElement expected, JsonElement actual, string path, ref string? mismatch)
    {
        if (expected.GetRawText() != actual.GetRawText())
        {
            mismatch = $"{path}: 期望 {expected.GetRawText()} 实际 {actual.GetRawText()}";
            return false;
        }
        return true;
    }

    // 简化 JSONPath：$ 开头、点路径、单索引 [n]
    public static string? EvaluateJsonPath(JsonElement root, string path)
    {
        if (!path.StartsWith('$')) return null;
        var current = root;
        var tokens = Regex.Matches(path[1..], @"(?:\.([A-Za-z0-9_-]+))|(?:\[(\d+)\])");
        foreach (Match token in tokens)
        {
            if (token.Groups[1].Success)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(token.Groups[1].Value, out current))
                    return null;
            }
            else if (token.Groups[2].Success)
            {
                if (current.ValueKind != JsonValueKind.Array) return null;
                var index = int.Parse(token.Groups[2].Value);
                var items = current.EnumerateArray().ToList();
                if (index >= items.Count) return null;
                current = items[index];
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString()
            : current.ValueKind == JsonValueKind.Null ? null
            : current.GetRawText();
    }

    public static string ResolveVariables(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (!template.Contains('{')) return template;
        return Regex.Replace(template, @"\{([A-Za-z0-9_]+)\}", match =>
            variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
