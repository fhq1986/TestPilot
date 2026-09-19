using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 自动登录的 storageState 缓存。
///
/// 这块的风险是**静默的错误复用**：缓存键少了一个维度（比如漏掉账号），
/// 就会把 A 账号的登录态装进 B 账号的执行里——用例可能"通过"，但验的根本不是那个人。
/// 所以下面的断言重点在键的构成与失效出口，而不只是"存取能对上"。
/// </summary>
public class AuthStateCacheTests
{
    private static AuthStateCache Build(int ttlMinutes) =>
        new(Options.Create(new ExecutionOptions { AuthStateTtlMinutes = ttlMinutes }),
            NullLogger<AuthStateCache>.Instance);

    private static Domain.Entities.Environment Env(string username, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = "测试环境",
        LoginUsername = username,
    };

    [Fact]
    public void 存取往返()
    {
        var cache = Build(30);
        const string key = "k1";

        cache.Set(key, """{"cookies":[{"name":"sid","value":"abc"}]}""");

        Assert.Equal("""{"cookies":[{"name":"sid","value":"abc"}]}""", cache.TryGet(key));
    }

    [Fact]
    public void 未写入时取到空()
    {
        Assert.Null(Build(30).TryGet("nope"));
    }

    [Fact]
    public void 作废后取不到()
    {
        var cache = Build(30);
        cache.Set("k1", "state");
        cache.Invalidate("k1");
        Assert.Null(cache.TryGet("k1"));
    }

    /// <summary>TTL &lt;= 0 视为关闭：设了也不该存，否则等于"关不掉的开关"</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 关闭时不缓存(int ttl)
    {
        var cache = Build(ttl);

        Assert.False(cache.Enabled);
        cache.Set("k1", "state");
        Assert.Null(cache.TryGet("k1"));
    }

    [Fact]
    public void 空状态不写入()
    {
        var cache = Build(30);
        cache.Set("k1", "   ");
        Assert.Null(cache.TryGet("k1"));
    }

    // ------------------------------ 缓存键

    /// <summary>
    /// 键必须含**账号**：漏掉它就会把管理员的登录态用到普通用户账号的执行上，
    /// 而权限相关的断言会因此"通过"——这是最坏的一类假通过
    /// </summary>
    [Fact]
    public void 不同账号的键不同()
    {
        var envId = Guid.NewGuid();
        var admin = AuthStateCache.KeyOf(Env("admin", envId), "chromium");
        var tester = AuthStateCache.KeyOf(Env("tester", envId), "chromium");

        Assert.NotNull(admin);
        Assert.NotEqual(admin, tester);
    }

    /// <summary>不同浏览器的 storage state 结构不同，不能串用</summary>
    [Fact]
    public void 不同浏览器的键不同()
    {
        var envId = Guid.NewGuid();
        Assert.NotEqual(
            AuthStateCache.KeyOf(Env("admin", envId), "chromium"),
            AuthStateCache.KeyOf(Env("admin", envId), "firefox"));
    }

    /// <summary>不同环境的登录态当然不能混用（不同站点的 cookie）</summary>
    [Fact]
    public void 不同环境的键不同()
    {
        Assert.NotEqual(
            AuthStateCache.KeyOf(Env("admin", Guid.NewGuid()), "chromium"),
            AuthStateCache.KeyOf(Env("admin", Guid.NewGuid()), "chromium"));
    }

    [Fact]
    public void 没有登录账号时不给键()
    {
        // 环境没配自动登录 → 无键可缓存（TryGet 传 null 会走不到，这里守住 KeyOf 的契约）
        var env = new Domain.Entities.Environment { Id = Guid.NewGuid(), Name = "x", LoginUsername = null };
        Assert.Null(AuthStateCache.KeyOf(env, "chromium"));
        Assert.Null(AuthStateCache.KeyOf(null, "chromium"));
    }

    // ------------------------------ 过期边界

    [Fact]
    public void 到期时刻即视为过期()
    {
        var now = DateTime.UtcNow;
        // 边界：恰好等于到期时间应判过期（写反成 < 会让条目多活一个 TTL 周期）
        Assert.True(AuthStateCache.IsExpired(now, now));
        Assert.True(AuthStateCache.IsExpired(now.AddSeconds(-1), now));
        Assert.False(AuthStateCache.IsExpired(now.AddSeconds(1), now));
    }
}
