using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 项目级 API Token 的纯逻辑：生成格式、哈希稳定性、可用性判定。
/// 这里钉住的三件事都是"坏了很难发现"的类型：
/// 前缀变了 CI 脚本里的正则会静默失配；哈希方式一变所有存量令牌立即失效；
/// IsActive 口径错会让"已吊销"变成"还能用"。
/// </summary>
public class ApiTokenServiceTests
{
    private static readonly DateTime Now = DateTime.Parse("2026-09-17T12:00:00Z").ToUniversalTime();

    [Fact]
    public void Generate_返回固定前缀且哈希稳定()
    {
        var (plain, hash, prefix) = ApiTokenService.Generate();

        // 格式：atp_ 开头 + base64url 字符集（无 + / =，放进 HTTP 头不用再转义）
        Assert.StartsWith("atp_", plain);
        Assert.DoesNotContain("+", plain);
        Assert.DoesNotContain("/", plain);
        Assert.DoesNotContain("=", plain);
        Assert.InRange(plain.Length, 41, 120);
        Assert.Equal(plain[..12], prefix);
        // 哈希与明文对应且稳定、小写十六进制 64 位
        Assert.Equal(ApiTokenService.Hash(plain), hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void Generate_两次生成互不相同()
    {
        var a = ApiTokenService.Generate().PlainToken;
        var b = ApiTokenService.Generate().PlainToken;
        Assert.NotEqual(a, b);
        Assert.NotEqual(ApiTokenService.Hash(a), ApiTokenService.Hash(b));
    }

    [Theory]
    [InlineData("atp_Abc123def456_very_long_random_part_here", true)]
    [InlineData("other_prefix_abc", false)]
    [InlineData("atp_", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikePlatformToken_只认平台前缀与合理长度(string? token, bool expected)
        => Assert.Equal(expected, ApiTokenService.LooksLikePlatformToken(token));

    [Fact]
    public void IsActive_未吊销未过期才可用()
    {
        ProjectApiToken Make(DateTime? expires = null, DateTime? revoked = null) => new()
        {
            TokenHash = new string('a', 64), ExpiresAt = expires, RevokedAt = revoked,
        };

        // 永不过期
        Assert.True(ApiTokenService.IsActive(Make(), Now));
        // 未到期 / 恰好到期（边界：到点即失效）
        Assert.True(ApiTokenService.IsActive(Make(Now.AddSeconds(1)), Now));
        Assert.False(ApiTokenService.IsActive(Make(Now), Now));
        Assert.False(ApiTokenService.IsActive(Make(Now.AddSeconds(-1)), Now));
        // 已吊销：即使没过期也不可用
        Assert.False(ApiTokenService.IsActive(Make(revoked: Now.AddSeconds(-1)), Now));
        // 吊销与过期同时存在：不可用
        Assert.False(ApiTokenService.IsActive(Make(Now.AddDays(-1), Now.AddDays(-2)), Now));
    }

    [Fact]
    public void PickActive_只有哈希匹配且活跃的令牌才命中()
    {
        var (plain, hash, prefix) = ApiTokenService.Generate();
        var active = new ProjectApiToken
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "CI",
            TokenHash = hash, Prefix = prefix, CreatedAt = Now.AddDays(-1),
        };
        var revoked = new ProjectApiToken
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "old",
            TokenHash = ApiTokenService.Hash(ApiTokenService.Generate().PlainToken),
            Prefix = "atp_zzzzzzzz", CreatedAt = Now.AddDays(-9), RevokedAt = Now.AddHours(-1),
        };
        var expired = new ProjectApiToken
        {
            Id = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Name = "expired",
            TokenHash = ApiTokenService.Hash(ApiTokenService.Generate().PlainToken),
            Prefix = "atp_yyyyyyyy", CreatedAt = Now.AddDays(-30), ExpiresAt = Now.AddMinutes(-1),
        };

        // 正确令牌 → 命中且就是那把
        var hit = ApiTokenService.PickActive(new[] { revoked, active, expired }, plain, Now);
        Assert.Same(active, hit);

        // 错误令牌 / 格式不像 / 空 → 不命中
        Assert.Null(ApiTokenService.PickActive(new[] { revoked, active, expired }, "atp_wrongwrongwrong", Now));
        Assert.Null(ApiTokenService.PickActive(new[] { revoked, active, expired }, "not-a-platform-token", Now));
        Assert.Null(ApiTokenService.PickActive(new[] { revoked, active, expired }, null, Now));
        // 候选为空 → 不命中
        Assert.Null(ApiTokenService.PickActive(Array.Empty<ProjectApiToken>(), plain, Now));
    }
}
