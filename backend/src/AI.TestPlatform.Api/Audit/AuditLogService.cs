using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;

namespace AI.TestPlatform.Api.Audit;

/// <summary>
/// 审计落库服务。刻意使用 <see cref="IServiceScopeFactory"/> 开独立作用域：
/// - 与业务 DbContext 解耦，业务回滚也不影响审计记录；
/// - 避免在业务 DbContext 已被释放（响应已开始写出）时访问它；
/// - 审计写失败只记日志，绝不冒泡影响业务响应。
/// </summary>
public sealed class AuditLogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>敏感字段名（大小写不敏感），命中即替换为 ***</summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "newPassword", "oldPassword", "passwordHash", "confirmPassword",
        "token", "accessToken", "refreshToken", "secret", "apiKey", "webhookToken",
        "smtpPassword", "loginPassword", "clientSecret", "authorization",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IServiceScopeFactory scopeFactory, ILogger<AuditLogService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        if (await TryWriteAsync(entry, aggressive: false, ct))
            return;

        // 第一次落库失败了（多半是某段文本仍超出列宽，历史上真发生过）。
        // 审计的底线是「宁可内容被截短，也不能没有记录」——
        // 所以在退化模式下再写一次：所有长文本压到很短的预览。
        await TryWriteAsync(entry, aggressive: true, ct);
    }

    /// <summary>
    /// 写一条审计记录。返回是否成功；失败只记服务端日志，**绝不冒泡影响业务响应**。
    /// </summary>
    /// <param name="aggressive">
    /// 退化模式：把所有长文本压到极短。列宽与截断逻辑一旦有偏差，
    /// 这个模式仍能保证记录落库（只是内容更短）。
    /// </param>
    private async Task<bool> TryWriteAsync(AuditEntry entry, bool aggressive, CancellationToken ct)
    {
        // 退化模式的统一上限：远小于任何一列的宽度，留足余量
        const int shortCap = 400;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = entry.UserId,
                Username = entry.Username,
                UserRole = entry.UserRole,
                Action = entry.Action,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                ResourceName = Truncate(entry.ResourceName, aggressive ? shortCap : 300),
                Method = entry.Method,
                Path = Truncate(entry.Path, aggressive ? shortCap : 500) ?? string.Empty,
                StatusCode = entry.StatusCode,
                Succeeded = entry.Succeeded,
                Detail = Truncate(Sanitize(entry.Detail), aggressive ? shortCap : 2000),
                ResponseBody = aggressive
                    ? Truncate(Sanitize(entry.ResponseBody), shortCap)
                    : CapResponse(Sanitize(entry.ResponseBody)),
                IpAddress = entry.IpAddress,
                UserAgent = Truncate(entry.UserAgent, aggressive ? shortCap : 300),
                DurationMs = entry.DurationMs,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            // 审计写失败不能影响业务（但要在服务端日志里留痕，便于排查）
            _logger.LogError(ex, "审计日志写入失败{Mode}：{Action} {ResourceType} {ResourceId}",
                aggressive ? "（已退化为短摘要重试）" : string.Empty,
                entry.Action, entry.ResourceType, entry.ResourceId);
            return false;
        }
    }

    /// <summary>
    /// 对摘要做脱敏：递归遍历 JSON，命中 <see cref="SensitiveKeys"/> 的值替换为 ***。
    ///
    /// 解析不了 JSON 时退化为按 <c>key: value</c> / <c>key=value</c> 的文本扫描——
    /// **这条兜底路径不是可有可无的**：请求体可能是被截断的 JSON、
    /// urlencoded 表单（<c>password=xxx&amp;user=yyy</c>）、或干脆一段自由文本。
    /// 早先的兜底只认带引号的 <c>"key":"value"</c>，于是表单形态里的密码会原样落库。
    /// </summary>
    public static string? Sanitize(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return detail;

        var trimmed = detail.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail);
                var node = SanitizeNode(doc.RootElement);
                return JsonSerializer.Serialize(node, JsonOptions);
            }
            catch (JsonException)
            {
                // 落到文本兜底
            }
        }

        return SanitizeFreeText(detail);
    }

    /// <summary>
    /// 文本形态的兜底脱敏，覆盖两种写法：
    /// 1) <c>key: "value"</c> / <c>key="value"</c>（截断的 JSON、multipart 字段）；
    /// 2) <c>key=value</c> / <c>key: value</c>（urlencoded 表单、自由文本）。
    ///
    /// 只对 <see cref="SensitiveKeys"/> 里出现过的名字动手，其余原样保留——
    /// 过度替换会把审计内容本身毁掉。
    /// </summary>
    private static string SanitizeFreeText(string text)
    {
        // 带引号的值：保留原有的引号与分隔符形态，只把值换掉
        text = Regex.Replace(text,
            @"(?<lead>(?<k>[A-Za-z0-9_]+)\s*[:=]\s*)(?<q>[""'])(?<v>.*?)\k<q>",
            m => SensitiveKeys.Contains(m.Groups["k"].Value)
                ? $"{m.Groups["lead"].Value}{m.Groups["q"].Value}***{m.Groups["q"].Value}"
                : m.Value);

        // 不带引号的值：值到空白或分隔符为止
        return Regex.Replace(text,
            @"(?<lead>(?<k>[A-Za-z0-9_]+)\s*[:=]\s*)(?<v>[^\s&;,}""']+)",
            m => SensitiveKeys.Contains(m.Groups["k"].Value)
                ? $"{m.Groups["lead"].Value}***"
                : m.Value);
    }

    private static object? SanitizeNode(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(
            p => p.Name, p => SensitiveKeys.Contains(p.Name) ? (object?)"***" : SanitizeNode(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Select(SanitizeNode).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>
    /// 截断到列宽**以内**。
    ///
    /// ⚠ 必须把省略号也算进上限。早先写的是 `value[..max] + "..."`，实际长度是 **max + 3**，
    /// 超过列定义 → Postgres 22001 → 而审计写入失败是被 catch 掉的 → **整条记录静默消失**。
    /// 实测到的后果：只要请求体超过 2000 字符（例如带几步的用例创建/更新），
    /// 审计里压根就没有这条记录——比"记错内容"严重得多的是"以为记下了、其实没有"。
    ///
    /// 公开仅为可测（纯函数，与 <see cref="Sanitize"/> 同理）。
    /// </summary>
    public static string? Truncate(string? value, int max)
    {
        if (value is null || value.Length <= max)
            return value;

        const string marker = "...";
        return max <= marker.Length ? value[..max] : value[..(max - marker.Length)] + marker;
    }

    /// <summary>
    /// 响应摘要超长时的处理：**换成一个自描述的 JSON 信封**，而不是把 JSON 直接砍断。
    ///
    /// 为什么不做成普通的截断（像 Detail 那样补个 "..."）：
    /// 砍断的 JSON 既解析不了，也分不清"是被截断了"还是"本来就是这样"——
    /// 而审计内容唯一的价值就是"你看到的就是当时返回的"。
    ///
    /// ⚠ 长度必须 **≤ 列宽 4000**，否则 Postgres 报 22001，而审计写入失败是被 catch 掉的
    /// ——那会变成**静默丢记录**（用单测钉住了这个不变量）。
    ///
    /// 所以这里**不能用一个固定的 preview 长度**：preview 是 JSON 文本，
    /// 塞进信封时会再做一次 JSON 转义（`"` → `\"`、`\` → `\\`），长度接近翻倍。
    /// 一段含大量引号的正常 JSON 响应，按固定 3500 字符取预览就能把信封撑到 7000+。
    /// 因此改成"先取一段、实测、装不下就缩"，直到真的装得进列宽。
    ///
    /// 公开仅为可测（纯函数，与 <see cref="Sanitize"/> 同理）。
    /// </summary>
    public static string? CapResponse(string? value)
    {
        const int columnMax = 4000;

        if (value is null || value.Length <= columnMax)
            return value;

        var take = 3500;
        while (true)
        {
            var preview = value[..take];
            var envelope = JsonSerializer.Serialize(new
            {
                truncated = true,
                originalLength = value.Length,
                note = $"响应过长，仅保留前 {take} 个字符",
                preview,
            }, JsonOptions);

            // take 为 0 时信封只剩百来字节，必定装得下，所以这个循环一定会结束
            if (envelope.Length <= columnMax || take == 0)
                return envelope;

            take = take * 3 / 4;
        }
    }

    /// <summary>计时器，用于记录请求耗时</summary>
    public static Stopwatch StartTimer() => Stopwatch.StartNew();
}

/// <summary>审计条目（过滤器收集 → 调用 <see cref="AuditLogService"/>.WriteAsync）</summary>
public sealed record AuditEntry(
    Guid? UserId,
    string? Username,
    string? UserRole,
    string Action,
    string ResourceType,
    string? ResourceId,
    string? ResourceName,
    string Method,
    string Path,
    int StatusCode,
    bool Succeeded,
    string? Detail,
    string? IpAddress,
    string? UserAgent,
    int DurationMs,
    /// <summary>响应结果摘要（**脱敏前**，脱敏在 <see cref="WriteAsync"/> 里统一做）；采集不到时为 null</summary>
    string? ResponseBody = null);

/// <summary>
/// 端点上声明的审计元数据。<see cref="WithAuditAttribute"/> 之外还支持从路由推断。
/// </summary>
/// <param name="CaptureResponse">
/// 是否采集响应结果。**默认开**——"返回了什么"和"传进去什么"同样是排查依据：
/// 只有请求内容时，没法区分"保存失败"和"保存成功但前端没刷新"。
/// 需要显式关掉的只有返回体巨大且无审计价值的端点（批量导出、报告聚合）。
/// </param>
public sealed record AuditMetadata(string Action, string ResourceType, bool CaptureBody,
    bool CaptureResponse = true);
