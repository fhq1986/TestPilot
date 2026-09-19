using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class StepVariableResolverTests
{
    private static readonly Dictionary<string, string> Variables = new()
    {
        ["username"] = "alice",
        ["password"] = "p@ss'w\"ord",
        ["expected"] = "登录成功",
    };

    private static StepConfig Config() => new()
    {
        Url = "/login?u={{username}}",
        Value = "{{password}}",
        Body = "{\"user\":\"{{username}}\"}",
        Endpoint = "/api/users/{{username}}",
        Headers = new List<HeaderEntry> { new() { Name = "X-User", Value = "{{username}}" } },
        Selector = new SelectorConfig
        {
            Type = "css",
            Value = "#user-{{username}}",
            Description = "用户名输入框（{{username}}）",
        },
    };

    [Fact]
    public void 替换所有字段中的变量()
    {
        var resolved = StepVariableResolver.Resolve(Config(), Variables)!;

        Assert.Equal("/login?u=alice", resolved.Url);
        Assert.Equal("p@ss'w\"ord", resolved.Value);
        Assert.Equal("{\"user\":\"alice\"}", resolved.Body);
        Assert.Equal("/api/users/alice", resolved.Endpoint);
        Assert.Equal("alice", resolved.Headers![0].Value);
        Assert.Equal("#user-alice", resolved.Selector!.Value);
        Assert.Equal("用户名输入框（alice）", resolved.Selector!.Description);
    }

    [Fact]
    public void 不修改原始配置()
    {
        var origin = Config();
        StepVariableResolver.Resolve(origin, Variables);
        Assert.Equal("/login?u={{username}}", origin.Url);
        Assert.Equal("{{password}}", origin.Value);
    }

    [Fact]
    public void 未命中的占位符原样保留()
    {
        var config = new StepConfig { Url = "/x?code={{unknown}}" };
        var resolved = StepVariableResolver.Resolve(config, Variables)!;
        Assert.Equal("/x?code={{unknown}}", resolved.Url);
    }

    [Fact]
    public void 变量名大小写不敏感()
    {
        var config = new StepConfig { Value = "{{USERNAME}}" };
        var resolved = StepVariableResolver.Resolve(config, Variables)!;
        Assert.Equal("alice", resolved.Value);
    }

    [Fact]
    public void 无变量时直接复用原对象()
    {
        var config = new StepConfig { Url = "/static" };
        Assert.Same(config, StepVariableResolver.Resolve(config, null));
        Assert.Same(config, StepVariableResolver.Resolve(config, new Dictionary<string, string>()));
    }

    [Fact]
    public void 内置函数生成动态值()
    {
        var config = new StepConfig { Url = "/order/{{$uuid}}?t={{$timestamp}}&n={{$random(100,200)}}" };
        var resolved = StepVariableResolver.Resolve(config, null)!;

        Assert.DoesNotContain("{{", resolved.Url);
        var parts = resolved.Url!.Split("?t=");
        Assert.True(Guid.TryParse(parts[0].Replace("/order/", string.Empty), out _));
        var random = parts[1].Split("&n=")[1];
        var value = int.Parse(random);
        Assert.InRange(value, 100, 200);
    }

    [Fact]
    public void 同一次替换内uuid保持一致()
    {
        var config = new StepConfig { Url = "/a/{{$uuid}}", Value = "{{$uuid}}" };
        var resolved = StepVariableResolver.Resolve(config, null)!;
        Assert.Equal(resolved.Url!.Replace("/a/", string.Empty), resolved.Value);
    }

    [Theory]
    [InlineData("{{a}}", true)]
    [InlineData("plain", false)]
    [InlineData("{{ a }}", true)]
    public void 占位符检测(string text, bool expected)
    {
        Assert.Equal(expected, StepVariableResolver.ContainsPlaceholder(new StepConfig { Value = text }));
    }

    [Fact]
    public void 收集引用到的变量名_排除内置函数()
    {
        var config = new StepConfig
        {
            Url = "/x?u={{username}}&t={{$timestamp}}",
            Value = "{{password}}",
            Selector = new SelectorConfig { Type = "css", Value = "#{{username}}" },
        };

        var keys = StepVariableResolver.CollectKeys(config);

        Assert.Equal(new[] { "password", "username" }, keys.ToArray());
    }
}

public class BrowserCatalogTests
{
    [Theory]
    [InlineData("chromium", "chromium")]
    [InlineData("chrome", "chromium")]
    [InlineData("Chrome", "chromium")]
    [InlineData("chrome-headless", "chromium")]
    [InlineData("Google Chrome", "chromium")]
    [InlineData("msedge", "chromium")]
    [InlineData("firefox", "firefox")]
    [InlineData("FF", "firefox")]
    [InlineData("gecko", "firefox")]
    [InlineData("webkit", "webkit")]
    [InlineData("Safari", "webkit")]
    [InlineData("safari-technology-preview", "webkit")]
    public void 别名归一化(string input, string expected)
        => Assert.Equal(expected, BrowserCatalog.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空值回落到默认浏览器(string? input)
        => Assert.Equal(BrowserCatalog.Default, BrowserCatalog.Normalize(input));

    [Fact]
    public void 无法识别的写法回落默认但会被标记为不识别()
    {
        Assert.Equal("chromium", BrowserCatalog.Normalize("netscape"));
        Assert.False(BrowserCatalog.IsRecognized("netscape"));
        Assert.True(BrowserCatalog.IsRecognized("firefox"));
        Assert.True(BrowserCatalog.IsRecognized(null));
    }

    [Fact]
    public void 展示名可用于界面与报告()
    {
        Assert.Equal("Chromium", BrowserCatalog.DisplayName("chrome"));
        Assert.Equal("Firefox", BrowserCatalog.DisplayName("ff"));
        Assert.Equal("WebKit", BrowserCatalog.DisplayName("safari"));
    }
}

public class ExecutionPlannerTests
{
    [Fact]
    public void 数据行标签截断到三个非空列()
    {
        var columns = new[] { "username", "password", "remark", "extra" };
        var row = new Dictionary<string, string>
        {
            ["username"] = "alice",
            ["password"] = "secret",
            ["remark"] = "",
            ["extra"] = "x",
        };

        var label = ExecutionPlanner.BuildRowLabel(columns, row, 0);

        Assert.StartsWith("#1 ", label);
        Assert.Contains("username=alice", label);
        Assert.Contains("password=secret", label);
        Assert.Contains("extra=x", label);
        Assert.DoesNotContain("remark", label);
    }

    [Fact]
    public void 空行也有可读标签()
    {
        Assert.Equal("#3", ExecutionPlanner.BuildRowLabel(new[] { "a" }, new Dictionary<string, string>(), 2));
    }

    [Fact]
    public void 超长单元格值被截断()
    {
        var longValue = new string('x', 100);
        var label = ExecutionPlanner.BuildRowLabel(new[] { "col" },
            new Dictionary<string, string> { ["col"] = longValue }, 0);

        Assert.Contains("…", label);
        Assert.True(label.Length <= 300);
    }
}
