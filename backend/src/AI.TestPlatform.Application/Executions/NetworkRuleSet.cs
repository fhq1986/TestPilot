using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.TestPlatform.Application.Executions;

/// <summary>用例级网络规则的动作</summary>
public enum NetworkRuleAction
{
    /// <summary>直接返回构造好的响应（桩掉第三方依赖的主力用这个）</summary>
    Fulfill,

    /// <summary>让请求失败，模拟网络不可达（用于验证前端的降级/错误提示）</summary>
    Abort,

    /// <summary>先延迟再放行，模拟慢接口（用于验证 loading 态与超时处理）</summary>
    Delay,
}

/// <summary>
/// 一条网络规则。
///
/// 字段刻意保持扁平可选：规则是**用例作者手填**的，多层嵌套会让"填错一个字段"变得很难看出。
/// </summary>
public record NetworkRule(
    /// <summary>URL 匹配，Playwright glob，如 <c>**/api/pay**</c></summary>
    string Pattern,
    NetworkRuleAction Action,
    /// <summary>Fulfill 的状态码，默认 200</summary>
    int? Status = null,
    /// <summary>Fulfill 的 Content-Type，默认 application/json</summary>
    string? ContentType = null,
    /// <summary>Fulfill 的响应体</summary>
    string? Body = null,
    /// <summary>Delay 的延迟毫秒数</summary>
    int? DelayMs = null);

/// <summary>
/// 网络规则的解析与校验。
///
/// 为什么把校验放在这里而不是直接反序列化：规则最终由 <c>page.RouteAsync</c> 消费，
/// 一个空 pattern 或越界的状态码会在**执行期**才炸，而且报错信息是 Playwright 的底层异常。
/// 在这里挡掉，用户拿到的是"哪条规则、哪个字段、错在哪"。
/// </summary>
public static class NetworkRuleSet
{
    /// <summary>单条用例的规则数上限（规则是逐条注册的，几十条以上说明该考虑用 mock 服务了）</summary>
    public const int MaxRules = 30;

    /// <summary>单条响应体上限（字符）。这是"构造响应"，不是数据搬运</summary>
    public const int MaxBodyChars = 100_000;

    /// <summary>最大延迟，避免把用例超时当睡眠用</summary>
    public const int MaxDelayMs = 120_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // 不转义中文：规则里的响应体常含中文，转义后查库排查会很难读
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 动作允许写可读的 "fulfill"/"abort"/"delay"，而不是 0/1/2——
        // 规则是用例作者手填的，看不见的数字没法自证对错。
        // （转换器默认仍接受数字，所以老数据/程序化写入不受影响。）
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// 解析并校验。返回 null 表示"没有配置规则"（与"配置了空数组"同义，都当作不拦截）。
    /// 非法内容抛 <see cref="InvalidOperationException"/>，由端点转成 400。
    /// </summary>
    public static IReadOnlyList<NetworkRule>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        List<NetworkRule>? rules;
        try
        {
            // PropertyNameCaseInsensitive 由 Web 默认提供，所以 camelCase / PascalCase 都能读
            rules = JsonSerializer.Deserialize<List<NetworkRule>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"网络规则不是合法 JSON：{ex.Message}");
        }

        if (rules is null || rules.Count == 0)
            return null;

        if (rules.Count > MaxRules)
            throw new InvalidOperationException($"网络规则最多 {MaxRules} 条，当前 {rules.Count} 条");

        for (var i = 0; i < rules.Count; i++)
            Validate(rules[i], i + 1);

        return rules;
    }

    /// <summary>序列化成入库形态（camelCase，与全项目 JSON 约定一致）</summary>
    public static string? Serialize(IReadOnlyList<NetworkRule>? rules) =>
        rules is null || rules.Count == 0 ? null : JsonSerializer.Serialize(rules, JsonOptions);

    /// <summary>校验后规范化：解析 → 校验 → 重新序列化，保证库里只有一种形态</summary>
    public static string? Normalize(string? json) => Serialize(Parse(json));

    private static void Validate(NetworkRule rule, int index)
    {
        var where = $"第 {index} 条网络规则";

        if (string.IsNullOrWhiteSpace(rule.Pattern))
            throw new InvalidOperationException($"{where} 缺少 URL 匹配（pattern），例如 **/api/pay**");
        if (rule.Pattern.Length > 500)
            throw new InvalidOperationException($"{where} 的 URL 匹配过长（上限 500 字符）");
        if (!Enum.IsDefined(rule.Action))
            // 别指望反序列化拦住：数值型 JSON 会被赋成未定义的枚举值
            throw new InvalidOperationException(
                $"{where} 的动作无效，可选：fulfill（返回构造响应）/ abort（请求失败）/ delay（延迟放行）");

        switch (rule.Action)
        {
            case NetworkRuleAction.Fulfill:
                if (rule.Status is < 100 or > 599)
                    throw new InvalidOperationException($"{where} 的返回状态码必须在 100~599 之间");
                if (rule.Body is { Length: > MaxBodyChars })
                    throw new InvalidOperationException(
                        $"{where} 的响应体过长（上限 {MaxBodyChars} 字符）");
                break;

            case NetworkRuleAction.Delay:
                if (rule.DelayMs is < 0 or > MaxDelayMs)
                    throw new InvalidOperationException($"{where} 的延迟毫秒数必须在 0~{MaxDelayMs} 之间");
                break;

            case NetworkRuleAction.Abort:
                // abort 不产生响应，带了 status/body 说明作者理解错了，直接提示而不是静默忽略
                if (rule.Status is not null || !string.IsNullOrEmpty(rule.Body))
                    throw new InvalidOperationException(
                        $"{where} 的动作是 abort（让请求失败），不应再填状态码或响应体");
                break;
        }
    }
}
