using System.Text.RegularExpressions;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Executions;

/// <summary>
/// 步骤变量替换：把步骤配置里的 {{变量名}} 替换成数据集行 / 执行变量的值。
///
/// 支持写法：
/// - <c>{{username}}</c>            → 直接取值
/// - <c>{{ $timestamp }}</c>        → 内置函数：当前毫秒时间戳（便于造唯一数据）
/// - <c>{{ $random(1000,9999) }}</c> → 内置函数：区间内随机整数
/// - <c>{{ $uuid }}</c>             → 内置函数：随机 GUID
/// - 未命中的占位符原样保留（便于发现拼写错误，而不是静默变成空串）
/// </summary>
public static class StepVariableResolver
{
    private static readonly Regex Placeholder = new(@"\{\{\s*(?<key>[^{}]+?)\s*\}\}", RegexOptions.Compiled);

    // 内置函数在单次替换内保持稳定（同一执行里多次引用 $uuid 得到同一个值）
    private const string UuidKey = "$uuid";

    /// <summary>
    /// 替换步骤配置中的所有占位符。返回新对象，不修改入参（步骤快照仍保留原始模板）。
    /// </summary>
    public static StepConfig? Resolve(StepConfig? config, IReadOnlyDictionary<string, string>? variables)
    {
        if (config is null) return null;

        // 无变量且配置里没有内置函数占位符时直接返回，避免无谓的对象复制
        var hasPlaceholder = ContainsPlaceholder(config);
        if (!hasPlaceholder) return config;

        var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (variables is not null)
        {
            foreach (var (key, value) in variables) cache[key] = value;
        }

        // 同一次 Resolve 内 $uuid 保持一致（跨字段引用同一 ID 时才有意义）
        var uuid = Guid.NewGuid().ToString();
        string ResolveText(string text) => Replace(text, cache, uuid);

        var resolved = new StepConfig
        {
            Url = config.Url is null ? null : ResolveText(config.Url),
            Method = config.Method,
            Endpoint = config.Endpoint is null ? null : ResolveText(config.Endpoint),
            Body = config.Body is null ? null : ResolveText(config.Body),
            Value = config.Value is null ? null : ResolveText(config.Value),
            Headers = config.Headers?.Select(h => new HeaderEntry
            {
                Name = h.Name,
                Value = ResolveText(h.Value ?? string.Empty),
            }).ToList(),
            Selector = config.Selector is null ? null : new SelectorConfig
            {
                Type = config.Selector.Type,
                Value = config.Selector.Value is null ? null : ResolveText(config.Selector.Value),
                Description = config.Selector.Description is null ? null : ResolveText(config.Selector.Description),
            },
        };
        return resolved;
    }

    /// <summary>是否包含 {{...}} 占位符</summary>
    public static bool ContainsPlaceholder(StepConfig? config)
    {
        if (config is null) return false;
        return Has(config.Url) || Has(config.Endpoint) || Has(config.Body) || Has(config.Value)
               || Has(config.Selector?.Value) || Has(config.Selector?.Description)
               || (config.Headers?.Any(h => Has(h.Value)) ?? false);

        static bool Has(string? text) => !string.IsNullOrEmpty(text) && text.Contains("{{");
    }

    /// <summary>
    /// 替换一段文本中的占位符（未命中则保留原样）。
    /// uuid 由调用方注入时可在同一次执行内复用，避免每个字段生成不同的值。
    /// </summary>
    public static string Replace(string text, IDictionary<string, string> variables, string? uuid = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{")) return text;
        uuid ??= Guid.NewGuid().ToString();

        return Placeholder.Replace(text, match =>
        {
            var key = match.Groups["key"].Value.Trim();
            if (variables.TryGetValue(key, out var value)) return value;
            // 大小写不敏感兜底：数据集列名大小写不一致时也能命中
            var hit = variables.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(hit.Key)) return hit.Value;

            var builtin = ResolveBuiltin(key, uuid);
            return builtin ?? match.Value;
        });
    }

    /// <summary>内置函数：以 $ 开头的占位符不需要用户提供值</summary>
    private static string? ResolveBuiltin(string key, string uuid)
    {
        if (key.Equals(UuidKey, StringComparison.OrdinalIgnoreCase)) return uuid;
        if (key.Equals("$timestamp", StringComparison.OrdinalIgnoreCase))
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        if (key.Equals("$datetime", StringComparison.OrdinalIgnoreCase))
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (key.Equals("$date", StringComparison.OrdinalIgnoreCase))
            return DateTime.Now.ToString("yyyy-MM-dd");
        if (key.Equals("$now", StringComparison.OrdinalIgnoreCase))
            return DateTime.Now.ToString("yyyyMMddHHmmss");

        var random = Regex.Match(key, @"^\$random\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)$", RegexOptions.IgnoreCase);
        if (random.Success && long.TryParse(random.Groups[1].Value, out var min) &&
            long.TryParse(random.Groups[2].Value, out var max))
        {
            if (min > max) (min, max) = (max, min);
            if (max - min > int.MaxValue) max = min + int.MaxValue;
            return Random.Shared.Next((int)min, (int)max + 1).ToString();
        }

        return null;
    }

    /// <summary>收集配置里引用到的变量名（用于校验与界面提示，不含内置函数）</summary>
    public static IReadOnlyList<string> CollectKeys(StepConfig? config)
    {
        if (config is null) return Array.Empty<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Scan(string? text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("{{")) return;
            foreach (Match match in Placeholder.Matches(text))
            {
                var key = match.Groups["key"].Value.Trim();
                if (!key.StartsWith('$')) keys.Add(key);
            }
        }

        Scan(config.Url);
        Scan(config.Endpoint);
        Scan(config.Body);
        Scan(config.Value);
        Scan(config.Selector?.Value);
        Scan(config.Selector?.Description);
        if (config.Headers is not null)
        {
            foreach (var header in config.Headers) Scan(header.Value);
        }
        return keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
