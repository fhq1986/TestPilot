using System.Security.Cryptography;
using AI.TestPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Auth;

/// <summary>
/// 项目级 API Token 的生成与校验。
///
/// 令牌格式：atp_ + 32 字节随机数的 base64url（无填充，总长约 53 字符）。
/// 库里只存 SHA-256 哈希——明文仅创建时返回一次，之后任何人（包括管理员）都拿不回来。
/// Prefix 存前 12 个字符用于列表辨认，不构成泄露（无法由前缀反推令牌）。
/// </summary>
public static class ApiTokenService
{
    /// <summary>令牌前缀：一眼可辨「这是本平台的令牌」，也便于密钥扫描器归类</summary>
    public const string TokenPrefix = "atp_";

    /// <summary>Prefix 列保存的长度（含 atp_）。8 位随机已足够区分同一项目的若干令牌</summary>
    public const int PrefixLength = 12;

    /// <summary>生成新令牌。返回 (明文, 要入库的哈希, 要入库的前缀)</summary>
    public static (string PlainToken, string Hash, string Prefix) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var random = Convert.ToBase64String(bytes).TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
        var plain = TokenPrefix + random;
        return (plain, Hash(plain), plain[..PrefixLength]);
    }

    /// <summary>SHA-256 的小写十六进制。与库里的 TokenHash 直接比对</summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>格式是否像本平台的令牌（快速短路，避免拿任意字符串查库）</summary>
    public static bool LooksLikePlatformToken(string? token) =>
        !string.IsNullOrEmpty(token) &&
        token.StartsWith(TokenPrefix, StringComparison.Ordinal) &&
        token.Length is > 40 and < 120;

    /// <summary>令牌当前是否可用（未吊销、未过期）。时间统一 UtcNow 口径</summary>
    public static bool IsActive(ProjectApiToken token, DateTime utcNow) =>
        token.RevokedAt is null &&
        (token.ExpiresAt is null || token.ExpiresAt.Value > utcNow);

    /// <summary>
    /// 在候选令牌里选出与 Bearer 令牌匹配且可用的那一把（纯函数，可单测）。
    /// </summary>
    public static ProjectApiToken? PickActive(
        IReadOnlyList<ProjectApiToken> candidates, string? bearerToken, DateTime utcNow)
    {
        if (!LooksLikePlatformToken(bearerToken) || candidates.Count == 0) return null;

        var hash = Hash(bearerToken!);
        var token = candidates.FirstOrDefault(t => t.TokenHash == hash);
        return token is not null && IsActive(token, utcNow) ? token : null;
    }

    /// <summary>
    /// 按 Bearer 令牌查找可用的项目令牌。
    /// 查询按 TokenHash 精确匹配（有索引），格式不像的直接返回 null 不查库；
    /// 命中与否的判定全部收敛在 <see cref="PickActive"/>。
    /// </summary>
    public static async Task<ProjectApiToken?> FindActiveAsync(
        DbContext db, string? bearerToken, DateTime utcNow, CancellationToken ct)
    {
        if (!LooksLikePlatformToken(bearerToken)) return null;

        var hash = Hash(bearerToken!);
        var candidates = await db.Set<ProjectApiToken>().AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .ToListAsync(ct);
        return PickActive(candidates, bearerToken, utcNow);
    }
}
