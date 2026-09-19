using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Domain.Entities;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Modules.Defects;

/// <summary>推送结果。Ok 时 ExternalRef 形如 "jira:QA-123" / "zentao:88"，ExternalUrl 为可直达的浏览地址。</summary>
public sealed record ExternalPushResult(bool Ok, string ExternalRef, string ExternalUrl, string Error);

/// <summary>登录页/缺陷页展示用的已启用 Provider 信息</summary>
public sealed record ExternalProviderInfo(string Id, string Name, string BrowseBaseUrl);

/// <summary>
/// 外部缺陷系统推送器：把平台缺陷作为正式缺陷单创建到 Jira / 禅道。
/// 只做「创建 + 记录 ExternalRef」的单向推送（v1）——双向状态同步涉及两边状态机映射，
/// 等 v1 用起来后再按实际字段需要加。
/// </summary>
public class ExternalDefectPusher
{
    public const string JiraId = "jira";
    public const string ZentaoId = "zentao";

    private readonly ExternalDefectOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ExternalDefectPusher> _logger;

    public ExternalDefectPusher(IOptions<ExternalDefectOptions> options,
        IHttpClientFactory httpFactory, ILogger<ExternalDefectPusher> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>已启用的 Provider（缺陷页推送入口与前端构造跳转链接用）</summary>
    public IReadOnlyList<ExternalProviderInfo> EnabledProviders()
    {
        var list = new List<ExternalProviderInfo>();
        if (_options.Jira.Enabled)
            list.Add(new ExternalProviderInfo(JiraId, "Jira",
                _options.Jira.BaseUrl.TrimEnd('/') + "/browse/"));
        if (_options.Zentao.Enabled)
            list.Add(new ExternalProviderInfo(ZentaoId, "禅道",
                _options.Zentao.BaseUrl.TrimEnd('/') + "/bug-view-"));
        return list;
    }

    public bool IsEnabled(string providerId) => providerId switch
    {
        JiraId => _options.Jira.Enabled,
        ZentaoId => _options.Zentao.Enabled,
        _ => false,
    };

    /// <summary>
    /// 推送缺陷。providerId 必须已启用；网络/接口错误一律落为 Ok=false + 人类可读 Error，
    /// 由调用方转 502 语义返回给前端展示。
    /// </summary>
    public async Task<ExternalPushResult> PushAsync(string providerId, Defect defect, CancellationToken ct)
    {
        try
        {
            return providerId switch
            {
                JiraId => await PushToJiraAsync(defect, ct),
                ZentaoId => await PushToZentaoAsync(defect, ct),
                _ => new ExternalPushResult(false, "", "", $"未知的外部缺陷系统：{providerId}"),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送缺陷 {DefectId} 到 {Provider} 失败", defect.Id, providerId);
            return new ExternalPushResult(false, "", "", $"推送失败：{ex.Message}");
        }
    }

    private async Task<ExternalPushResult> PushToJiraAsync(Defect defect, CancellationToken ct)
    {
        var cfg = _options.Jira;
        var baseUrl = cfg.BaseUrl.TrimEnd('/');

        var http = _httpFactory.CreateClient("external-defects");
        if (!string.IsNullOrWhiteSpace(cfg.ApiToken))
        {
            // Jira Cloud：Basic(email:apiToken)；Server/DC 填 用户名:密码 同样适用
            var raw = $"{cfg.Email}:{cfg.ApiToken}";
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
        }

        var payload = new
        {
            fields = new Dictionary<string, object>
            {
                ["project"] = new { key = cfg.ProjectKey },
                ["summary"] = Truncate(defect.Title, 250),
                // api/2 的 description 是**纯文本**（api/3 才强制 ADF）。
                // 平台里描述已是富文本 HTML，直接发过去收件人看到的是一堆标签，
                // 所以这里必须还原成纯文本，而不是原样透传。
                ["description"] = RichText.ToPlainText(defect.Description),
                ["issuetype"] = new { name = cfg.IssueType },
                ["labels"] = new[] { $"severity:{defect.Severity.ToString().ToLowerInvariant()}", "aitest" },
            },
        };

        var res = await http.PostAsJsonAsync($"{baseUrl}/rest/api/2/issue", payload, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return new ExternalPushResult(false, "", "", $"Jira 返回 {(int)res.StatusCode}：{Truncate(body, 300)}");

        var created = JsonSerializer.Deserialize<JiraCreateResponse>(body);
        if (string.IsNullOrEmpty(created?.Key))
            return new ExternalPushResult(false, "", "", "Jira 响应缺少 issue key");

        return new ExternalPushResult(true, $"{JiraId}:{created.Key}", $"{baseUrl}/browse/{created.Key}", "");
    }

    private async Task<ExternalPushResult> PushToZentaoAsync(Defect defect, CancellationToken ct)
    {
        var cfg = _options.Zentao;
        var baseUrl = cfg.BaseUrl.TrimEnd('/');

        var http = _httpFactory.CreateClient("external-defects");
        http.DefaultRequestHeaders.Add("Token", cfg.Token);
        http.DefaultRequestHeaders.Add("api-version", "1");

        // 禅道 severity：1 致命 … 4 建议性（与平台 DefectSeverity 顺序相反）
        var payload = new
        {
            title = Truncate(defect.Title, 250),
            severity = 4 - (int)defect.Severity, // Critical(3)->1, Suggestion(0)->4
            product = cfg.ProductId,
            // 禅道的 steps 是 **HTML** 字段（富文本），与平台的富文本描述同构，
            // 因此走 Normalize：新数据原样净化，历史纯文本会被包装成段落（换行不再丢）。
            steps = RichText.Normalize(defect.Description) ?? string.Empty,
            openedBy = "",
        };

        var res = await http.PostAsJsonAsync($"{baseUrl}/api.php/v1/bugs", payload, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return new ExternalPushResult(false, "", "", $"禅道返回 {(int)res.StatusCode}：{Truncate(body, 300)}");

        var created = JsonSerializer.Deserialize<ZentaoCreateResponse>(body);
        if (created?.Id is not int id)
            return new ExternalPushResult(false, "", "", "禅道响应缺少 bug id");

        return new ExternalPushResult(true, $"{ZentaoId}:{id}", $"{baseUrl}/bug-view-{id}.html", "");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private sealed record JiraCreateResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("key")] string? Key);

    private sealed record ZentaoCreateResponse(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("title")] string? Title);
}
