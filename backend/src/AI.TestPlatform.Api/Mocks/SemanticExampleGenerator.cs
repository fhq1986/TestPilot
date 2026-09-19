using System.Text.Json.Nodes;

namespace AI.TestPlatform.Api.Mocks;

// 按字段名语义 + 类型生成确定性示例值
public static class SemanticExampleGenerator
{
    // 顺序即优先级：更精确的键放前面（userid 先于 id）
    private static readonly (string[] Keys, object Value)[] KeywordMap =
    {
        (new[] { "userid", "user_id" }, 1001L),
        (new[] { "email" }, "user@example.com"),
        (new[] { "phone", "mobile" }, "13800138000"),
        (new[] { "price", "amount", "money" }, 99.99m),
        (new[] { "createdat", "updatedat", "date", "time" }, "2026-01-01T00:00:00"),
        (new[] { "name", "title" }, "示例名称"),
        (new[] { "status", "state" }, "active"),
        (new[] { "id" }, 1001L),
        (new[] { "tel" }, "13800138000"),
    };

    public static object Generate(string fieldName, string? type)
    {
        var key = fieldName.ToLowerInvariant();
        foreach (var (keys, value) in KeywordMap)
        {
            if (keys.Any(k => IsMatch(key, k)))
                return value;
        }
        return type switch
        {
            "integer" => 0L,
            "number" => 0.0,
            "boolean" => true,
            "array" => new JsonArray(),
            "object" => new JsonObject(),
            _ => "string",
        };
    }

    // 无 false positive 的匹配（valid/paid/guid 不再命中 id，hotel 不再命中 tel）：
    // 1) 完整词精确（如 createdAt -> createdat、user_id -> userid）
    // 2) camelCase/分隔符分词后 token 精确相等（userId -> [user, id]）
    // 3) 短关键词 id/tel 仅限前两种规则（不做后缀匹配，避免 valid/hotel 类误伤）；
    //    其余语义安全词允许后缀匹配（username -> name、starttime -> time）
    private static bool IsMatch(string key, string keyword)
    {
        if (key == keyword)
            return true;
        var normalized = key.Replace("_", "").Replace("-", "");
        if (normalized == keyword)
            return true;
        foreach (var token in SplitWords(key))
        {
            if (token == keyword)
                return true;
        }
        return keyword is not ("id" or "tel")
            && key.EndsWith(keyword, StringComparison.Ordinal);
    }

    private static IEnumerable<string> SplitWords(string key)
    {
        var buffer = new List<char>();
        foreach (var ch in key)
        {
            if (ch is '_' or '-')
            {
                if (buffer.Count > 0)
                {
                    yield return new string(buffer.ToArray());
                    buffer.Clear();
                }
                continue;
            }
            if (buffer.Count > 0 && char.IsUpper(ch))
            {
                yield return new string(buffer.ToArray());
                buffer.Clear();
            }
            buffer.Add(char.ToLowerInvariant(ch));
        }
        if (buffer.Count > 0)
            yield return new string(buffer.ToArray());
    }
}
