using AI.TestPlatform.Application.ApiTesting;

namespace AI.TestPlatform.UnitTests;

public class UrlResolverTests
{
    [Fact]
    public void Resolve_AbsoluteUrl_Unchanged()
    {
        var result = UrlResolver.Resolve("http://example.com/a", "http://base.com");

        Assert.Equal("http://example.com/a", result);
    }

    [Fact]
    public void Resolve_RelativeUrl_JoinsBaseUrl()
    {
        var result = UrlResolver.Resolve("login", "http://base.com");

        Assert.Equal("http://base.com/login", result);
    }

    [Fact]
    public void Resolve_BaseUrlTrailingSlash_Deduplicated()
    {
        var result = UrlResolver.Resolve("login", "http://base.com/");

        Assert.Equal("http://base.com/login", result);
    }

    [Fact]
    public void Resolve_UrlLeadingSlash_Deduplicated()
    {
        var result = UrlResolver.Resolve("/login", "http://base.com");

        Assert.Equal("http://base.com/login", result);
    }

    [Fact]
    public void Resolve_NoBaseUrl_RelativeReturnedAsIs()
    {
        var result = UrlResolver.Resolve("/main", null);

        Assert.Equal("/main", result);
    }
}
