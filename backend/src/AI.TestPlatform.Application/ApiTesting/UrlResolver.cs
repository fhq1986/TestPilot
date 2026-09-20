namespace AI.TestPlatform.Application.ApiTesting;

// URL 解析纯函数：绝对 URL 原样返回；相对路径拼接环境 BaseUrl
public static class UrlResolver
{
    public static string Resolve(string? url, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? string.Empty;

        // 只有带 http/https scheme 的才算「真正的绝对 URL」，原样返回。
        // ⚠️ 不能只判断 Uri.TryCreate(url, UriKind.Absolute)：在 Unix（Linux 容器）上，
        // 以 "/" 开头的站内路径（如 "/login"）会被 .NET 当成绝对 file 路径而返回 true，
        // 相对路径于是被原样返回、不拼 BaseUrl，Playwright 直接报
        // "Cannot navigate to invalid URL"。这是线上自动登录全挂的根因。
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            return url;

        // 相对路径（含 "/login" 这类站内地址）：靠 BaseUrl 补全
        if (string.IsNullOrWhiteSpace(baseUrl))
            return url;

        return baseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
    }
}
