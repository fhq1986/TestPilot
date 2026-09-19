namespace AI.TestPlatform.Application.ApiTesting;

// URL 解析纯函数：绝对 URL 原样返回；相对路径拼接环境 BaseUrl
public static class UrlResolver
{
    public static string Resolve(string? url, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            return url;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return url;
        return baseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
    }
}
