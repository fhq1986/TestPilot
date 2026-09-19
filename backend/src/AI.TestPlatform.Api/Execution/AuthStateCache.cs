using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 自动登录得到的 storageState 缓存（**进程内，不落盘**）。
///
/// 为什么需要它：平台已有"环境级自动登录"（见 <see cref="TestRunner"/> 的自动登录前置），
/// 但它是**每条用例都登录一次**的。500 条用例跑一轮，就是 500 次登录：
/// 墙钟时间被乘以 500，而且登录接口只要抖一下，整批用例会**同时**变红——
/// 排查时会以为是功能坏了，其实是登录慢/失败。
///
/// 缓存按「环境 + 浏览器 + 登录账号」三个维度分桶：任何一维不同，登录态都不能复用
/// （不同账号的 cookie 完全不同；不同浏览器的 storage state 格式也不同）。
///
/// **为什么放内存而不是对象存储**：
/// 它是可再生的临时凭证，落盘/进对象存储等于多一份需要清理、需要保护的敏感数据；
/// 而多节点部署下各节点各登录一次，代价本来就可以接受。少一个要运维的东西。
///
/// 失效有两个出口：TTL 到期，以及**用到缓存的执行失败时主动作废**
/// （见 <see cref="TestRunner"/>）——后者是关键，否则一次登录态过期会让后面所有用例连续失败。
/// </summary>
public sealed class AuthStateCache
{
    private sealed record Entry(string State, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ExecutionOptions _options;
    private readonly ILogger<AuthStateCache> _logger;

    public AuthStateCache(IOptions<ExecutionOptions> options, ILogger<AuthStateCache> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>是否启用（TTL &lt;= 0 视为关闭）</summary>
    public bool Enabled => _options.AuthStateTtlMinutes > 0;

    /// <summary>
    /// 缓存键：环境 + 浏览器 + 登录账号。返回 null 表示"这个环境没有可复用的自动登录配置"。
    /// </summary>
    public static string? KeyOf(Domain.Entities.Environment? environment, string browserName)
        => environment is null || string.IsNullOrWhiteSpace(environment.LoginUsername)
            ? null
            : $"{environment.Id:N}|{browserName}|{environment.LoginUsername}";
    /// <summary>取未过期的登录态；过期或不存在返回 null（过期条目顺手删掉，不让它无限堆积）</summary>
    public string? TryGet(string key)
    {
        if (!Enabled) return null;
        if (!_entries.TryGetValue(key, out var entry)) return null;
        if (IsExpired(entry.ExpiresAtUtc, DateTime.UtcNow))
        {
            _entries.TryRemove(key, out _);
            return null;
        }
        return entry.State;
    }

    /// <summary>
    /// 过期判定。单独抽成纯函数是为了可测——缓存没有注入时钟，
    /// 端到端等一个 TTL 到期不现实，而这种"一行的边界判断"恰恰最容易写反（&lt; 写成 &lt;=）。
    /// </summary>
    public static bool IsExpired(DateTime expiresAtUtc, DateTime nowUtc) => expiresAtUtc <= nowUtc;

    public void Set(string key, string state)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(state)) return;
        _entries[key] = new Entry(state, DateTime.UtcNow.AddMinutes(_options.AuthStateTtlMinutes));
        _logger.LogInformation("已缓存登录态，{Minutes} 分钟内同环境的后续执行将复用它", _options.AuthStateTtlMinutes);
    }

    /// <summary>作废某条登录态（用到它却失败了，八成已经过期）</summary>
    public void Invalidate(string key)
    {
        if (_entries.TryRemove(key, out _))
            _logger.LogWarning("复用登录态的执行失败，已作废该登录态缓存（下次执行将重新登录）");
    }

    /// <summary>清空（仅供测试与手工排障）</summary>
    public void Clear() => _entries.Clear();
}
