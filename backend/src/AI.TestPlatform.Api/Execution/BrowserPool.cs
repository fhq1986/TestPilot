using System.Collections.Concurrent;
using AI.TestPlatform.Application.Executions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AI.TestPlatform.Api.Execution;

/// <summary>
/// 浏览器实例池（迭代 D）。
///
/// 之前每条执行都走 <c>Playwright.CreateAsync()</c> + <c>LaunchAsync()</c>：
/// 前者要拉起一个 Node 驱动进程，后者要冷启动一个浏览器，合起来 1~3 秒。
/// 100 条用例 × 2 浏览器就是几百次冷启动，是回归吞吐最大的单点浪费。
///
/// 池化后：
/// - <see cref="IPlaywright"/> 驱动进程全应用只起一个；
/// - 每个浏览器引擎（chromium/firefox/webkit）各保留一个 <see cref="IBrowser"/> 实例；
/// - 每次执行只新建 <see cref="IBrowserContext"/>——语境隔离该有的还有，冷启动开销没了。
///
/// 为什么必须显式释放 context：浏览器长期存活后，泄漏的 context 会在同一个浏览器里不断累积
/// （旧代码里 context 没释放但浏览器跟着执行一起销毁，所以问题被掩盖了）。
/// 调用方必须 <c>await using</c> 自己的 context。
/// </summary>
public sealed class BrowserPool : IAsyncDisposable
{
    /// <summary>串行化「首次创建 / 崩溃重建」，避免并发执行同时拉起多个浏览器</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, PooledEntry> _entries = new(StringComparer.Ordinal);
    private readonly ExecutionOptions _options;
    private readonly ILogger<BrowserPool> _logger;

    private IPlaywright? _playwright;
    private Timer? _trimTimer;
    private bool _disposed;

    public BrowserPool(IOptions<ExecutionOptions> options, ILogger<BrowserPool> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private sealed class PooledEntry
    {
        public required IBrowser Browser { get; init; }
        public required string Version { get; init; }
        /// <summary>最近一次被借出的时刻（<see cref="Environment.TickCount64"/>），用于空闲回收</summary>
        public long LastUsedTicks;
    }

    /// <summary>当前池里各引擎的版本（诊断/健康检查用）</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _entries.ToDictionary(kv => kv.Key, kv => kv.Value.Version);

    /// <summary>
    /// 取一个可用的浏览器实例。返回的 <see cref="IBrowser"/> **归池所有，调用方不要释放**，
    /// 只需在使用后释放自己创建的 <see cref="IBrowserContext"/>。
    /// </summary>
    public async Task<(IBrowser Browser, string Version)> GetAsync(string? browserName, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var engine = BrowserCatalog.Normalize(browserName);

        // 快路径：池里已有且连接正常，直接复用（绝大多数调用走这里，不进锁）
        if (_entries.TryGetValue(engine, out var cached) && cached.Browser.IsConnected)
        {
            cached.LastUsedTicks = Environment.TickCount64;
            return (cached.Browser, cached.Version);
        }

        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // 双检：等锁期间可能已被别的执行重建
            if (_entries.TryGetValue(engine, out var again) && again.Browser.IsConnected)
            {
                again.LastUsedTicks = Environment.TickCount64;
                return (again.Browser, again.Version);
            }

            // 走到这里说明首次启动，或上一次的浏览器已断开（崩溃/被外部结束）
            if (_entries.TryRemove(engine, out var dead))
            {
                _logger.LogWarning("浏览器 {Engine} 已断开（版本 {Version}），重新启动", engine, dead.Version);
                await SafeDisposeBrowserAsync(dead.Browser);
            }

            _playwright ??= await Playwright.CreateAsync();
            var browserType = engine switch
            {
                BrowserCatalog.Firefox => _playwright.Firefox,
                BrowserCatalog.Webkit => _playwright.Webkit,
                _ => _playwright.Chromium,
            };

            var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var entry = new PooledEntry
            {
                Browser = browser,
                Version = browser.Version,
                LastUsedTicks = Environment.TickCount64,
            };
            _entries[engine] = entry;

            // 浏览器进程崩掉时把池里的记录摘掉，下次借用会自动重建
            browser.Disconnected += (_, _) =>
            {
                if (_entries.TryGetValue(engine, out var current) &&
                    ReferenceEquals(current.Browser, browser))
                {
                    _entries.TryRemove(engine, out _);
                }
                _logger.LogWarning("浏览器 {Engine} 连接中断，已从池中移除", engine);
            };

            _logger.LogInformation("浏览器池：{Engine} 已启动，版本 {Version}", engine, entry.Version);
            EnsureTrimTimer();
            return (browser, entry.Version);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 空闲回收。执行器闲着的时候浏览器进程不该一直占着内存（一个 Chromium 常驻约 200MB）。
    /// 用池内定时器而不是新的 HostedService，避免为一个纯内部清理职责再加一个后台服务。
    /// </summary>
    private void EnsureTrimTimer()
    {
        if (_options.BrowserIdleTimeoutMinutes <= 0) return;
        if (_trimTimer is not null) return;
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.BrowserIdleTimeoutMinutes));
        _trimTimer = new Timer(_ => _ = TrimIdleAsync(), null, interval, interval);
    }

    /// <summary>关闭空闲超时的浏览器（公开出来便于测试与手动释放）</summary>
    public async Task<int> TrimIdleAsync()
    {
        if (_disposed) return 0;
        var timeoutMs = TimeSpan.FromMinutes(Math.Max(1, _options.BrowserIdleTimeoutMinutes)).TotalMilliseconds;
        if (_options.BrowserIdleTimeoutMinutes <= 0) return 0;

        var now = Environment.TickCount64;
        var closed = 0;

        await _gate.WaitAsync();
        try
        {
            foreach (var (engine, entry) in _entries.ToList())
            {
                if (now - entry.LastUsedTicks < timeoutMs) continue;
                if (!_entries.TryRemove(engine, out var removed)) continue;
                await SafeDisposeBrowserAsync(removed.Browser);
                _logger.LogInformation("浏览器池：{Engine} 空闲超时，已关闭释放内存", engine);
                closed++;
            }
        }
        finally
        {
            _gate.Release();
        }

        // 全空了就把驱动进程也收掉，并停掉定时器（下次借用会重新拉起）
        if (_entries.IsEmpty)
        {
            var timer = Interlocked.Exchange(ref _trimTimer, null);
            if (timer is not null) await timer.DisposeAsync();
        }
        return closed;
    }

    private async Task SafeDisposeBrowserAsync(IBrowser browser)
    {
        try
        {
            await browser.DisposeAsync();
        }
        catch (Exception ex)
        {
            // 浏览器已经崩了的情况下 Dispose 也会抛，这里只记日志
            _logger.LogDebug(ex, "释放浏览器实例时出错（通常无害）");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        var timer = Interlocked.Exchange(ref _trimTimer, null);
        if (timer is not null) await timer.DisposeAsync();

        foreach (var (_, entry) in _entries.ToList())
            await SafeDisposeBrowserAsync(entry.Browser);
        _entries.Clear();

        try
        {
            _playwright?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "释放 Playwright 驱动进程时出错（通常无害）");
        }
        _playwright = null;

        _gate.Dispose();
    }
}
