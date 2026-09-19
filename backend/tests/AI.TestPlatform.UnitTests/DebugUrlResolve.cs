using System;
using AI.TestPlatform.Application.ApiTesting;
using Xunit;

namespace AI.TestPlatform.UnitTests;

public class DebugUrlResolve
{
    [Fact]
    public void Test()
    {
        var baseUrl = "http://111.231.19.163:8088";

        Console.WriteLine("=== UrlResolver.Resolve tests ===");
        foreach (var url in new[] { "/login", "http://111.231.19.163:8088/login", "http://111.231.19.163:8088" })
        {
            var resolved = UrlResolver.Resolve(url, baseUrl);
            var tryCreate = Uri.TryCreate(resolved, UriKind.Absolute, out var uri);
            Console.WriteLine($"url=\"{url}\" => resolved=\"{resolved}\" TryCreate={tryCreate} AbsoluteUri={uri?.AbsoluteUri}");
        }

        Console.WriteLine("=== direct Uri.TryCreate tests ===");
        Console.WriteLine($"/login Absolute={Uri.TryCreate("/login", UriKind.Absolute, out _)}");
        Console.WriteLine($"http://x Absolute={Uri.TryCreate("http://x", UriKind.Absolute, out _)}");
        Console.WriteLine($"http://x/ Absolute={Uri.TryCreate("http://x/", UriKind.Absolute, out _)}");
    }
}
