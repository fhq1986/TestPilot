using AI.TestPlatform.Domain.Entities;
using Environment = AI.TestPlatform.Domain.Entities.Environment;

namespace AI.TestPlatform.Application.Environments;

public record EnvironmentView(
    Guid Id, Guid ProjectId, string Name, string BaseUrl,
    string? LoginUrl, string? LoginUsername,
    string LoginPasswordMasked, bool HasLoginPassword,
    string? LoginSuccessIndicator, bool AutoLogin,
    DateTime CreatedAt, DateTime UpdatedAt,
    /// <summary>环境默认浏览器（chromium / firefox / webkit）；为空表示跟随用例配置</summary>
    string? Browser = null);

public record CreateEnvironmentRequest(
    string Name, string BaseUrl, string? LoginUrl, string? LoginUsername,
    string? LoginPassword, string? LoginSuccessIndicator, bool AutoLogin,
    string? Browser = null);

public record UpdateEnvironmentRequest(
    string Name, string BaseUrl, string? LoginUrl, string? LoginUsername,
    string? LoginPassword, string? LoginSuccessIndicator, bool AutoLogin,
    string? Browser = null);

public static class EnvironmentDtos
{
    // 与系统配置一致的脱敏逻辑：≤8 字符全掩；否则前3 + *** + 后4
    public static string Mask(string? value) =>
        string.IsNullOrEmpty(value) || value.Length <= 8 ? "***"
        : $"{value[..3]}***{value[^4..]}";

    public static EnvironmentView ToView(Environment e) => new(
        e.Id, e.ProjectId, e.Name, e.BaseUrl,
        e.LoginUrl, e.LoginUsername,
        Mask(e.LoginPassword), !string.IsNullOrEmpty(e.LoginPassword),
        e.LoginSuccessIndicator, e.AutoLogin,
        e.CreatedAt, e.UpdatedAt, e.Browser);
}
