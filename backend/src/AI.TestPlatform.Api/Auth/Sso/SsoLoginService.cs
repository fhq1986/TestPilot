using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

namespace AI.TestPlatform.Api.Auth.Sso;

/// <summary>SSO 登录失败的原因（映射到不同的 HTTP 语义与提示文案）。</summary>
public enum SsoLoginFailure
{
    /// <summary>授权码无效 / 已使用 / 企业侧校验失败</summary>
    InvalidCode,

    /// <summary>账号未绑定且未开启自动开通（或账号被停用）</summary>
    NotLinked,
}

/// <summary>
/// SSO 登录编排：企业身份 → 平台账号匹配/绑定/开通 → 签发平台 JWT。
/// 账号解析顺序：
///   1. (Provider, Subject) 已绑定 → 直接登录；
///   2. 企业身份带邮箱且**恰好只有一个**启用账号使用该邮箱 → 自动绑定该账号并登录
///      （邮箱不唯一是平台的合法用法，见 User.Email 注释，多于一个时不猜，落到底）；
///   3. 开启 AutoProvision → 自动开通 Viewer 账号；
///   4. 否则拒绝，提示联系管理员。
/// 生效配置来自系统设置页（SettingsService，保存即生效）；appsettings 的 Sso 节只是首次种子。
/// </summary>
public class SsoLoginService
{
    /// <summary>防 CSRF 的 state 缓存时长：企业授权页停留时间上限。</summary>
    public const int StateLifetimeSeconds = 600;

    private readonly TestDbContext _db;
    private readonly IAuthService _authService;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _services;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SsoLoginService> _logger;
    private readonly IHostEnvironment _env;

    public SsoLoginService(
        TestDbContext db,
        IAuthService authService,
        SettingsService settings,
        IServiceProvider services,
        IMemoryCache cache,
        ILogger<SsoLoginService> logger,
        IHostEnvironment env)
    {
        _db = db;
        _authService = authService;
        _settings = settings;
        _services = services;
        _cache = cache;
        _logger = logger;
        _env = env;
    }

    /// <summary>读取当前生效的 SSO 配置（设置页保存即生效）。每次 SSO 请求读一次单行配置表，开销可忽略。</summary>
    public Task<SsoOptions> GetEffectiveOptionsAsync(CancellationToken ct) => _settings.GetSsoOptionsAsync(ct);

    /// <summary>解析已注册的 Provider 实例；未注册或未启用返回 null。</summary>
    public ISsoProvider? ResolveProvider(string providerId, SsoOptions options)
    {
        if (!options.Providers.TryGetValue(providerId, out var config) || !config.Enabled)
            return null;

        // 安全审查 S5：mock Provider 的 code 就是明文用户名（mock:{用户名}），能伪造任意企业身份。
        // 只在 Development 允许，配置误开也进不了非开发环境——不依赖运维记得关开关
        if (providerId.Equals("mock", StringComparison.OrdinalIgnoreCase) && !_env.IsDevelopment())
        {
            _logger.LogWarning("忽略 mock SSO Provider：仅在 Development 环境可用（当前环境 {Environment}）", _env.EnvironmentName);
            return null;
        }

        return providerId.ToLowerInvariant() switch
        {
            "wecom" => ActivatorUtilities.CreateInstance<WecomSsoProvider>(_services, config),
            "dingtalk" => ActivatorUtilities.CreateInstance<DingtalkSsoProvider>(_services, config),
            "mock" => ActivatorUtilities.CreateInstance<MockSsoProvider>(_services, config),
            _ => null,
        };
    }

    /// <summary>生成并缓存一次性 state（防 CSRF：回调必须带回同值才肯换 token/绑定）。
    /// mode 记进缓存：login 换登录态、bind 只允许用于绑定，两类回调互不能混用。</summary>
    public string NewState(string providerId, string mode = "login")
    {
        var state = Guid.NewGuid().ToString("N");
        _cache.Set(StateKey(state), (providerId, mode), TimeSpan.FromSeconds(StateLifetimeSeconds));
        return state;
    }

    /// <summary>校验并**消费** state（一次性），匹配时返回 true 并带出 mode。</summary>
    public bool TryConsumeState(string state, string providerId, out string mode)
    {
        mode = "login";
        if (_cache.TryGetValue(StateKey(state), out (string ProviderId, string Mode)? entry)
            && entry is not null
            && entry.Value.ProviderId == providerId)
        {
            _cache.Remove(StateKey(state));
            mode = entry.Value.Mode;
            return true;
        }
        return false;
    }

    private static string StateKey(string state) => $"sso:state:{state}";

    /// <summary>企业授权完成后的前端回调页地址（{FrontendBaseUrl}/login/sso）。</summary>
    public static string FrontendCallbackUrl(SsoOptions options)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.FrontendBaseUrl)
            ? "http://localhost:5173"
            : options.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}/login/sso";
    }

    /// <summary>
    /// 企业授权码换平台登录态。成功返回 <see cref="AuthResult"/>；
    /// 失败返回 null 并给出 <see cref="SsoLoginFailure"/>。
    /// </summary>
    public async Task<(AuthResult? Result, SsoLoginFailure? Failure)> LoginAsync(
        string providerId, string code, string? ip, CancellationToken ct)
    {
        var options = await GetEffectiveOptionsAsync(ct);
        var provider = ResolveProvider(providerId, options);
        if (provider is null)
            return (null, SsoLoginFailure.NotLinked);

        var identity = await provider.ExchangeAsync(code, ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.Subject))
            return (null, SsoLoginFailure.InvalidCode);

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.SsoProvider == provider.Id && u.SsoSubject == identity.Subject, ct);

        if (user is null && !string.IsNullOrWhiteSpace(identity.Email))
        {
            // 邮箱唯一匹配才自动绑定——多个账号同邮箱时不猜（见 User.Email 的非唯一设计）
            var emailMatches = await _db.Users
                .Where(u => u.Email == identity.Email && u.IsActive)
                .ToListAsync(ct);
            if (emailMatches.Count == 1)
            {
                user = emailMatches[0];
                user.SsoProvider = provider.Id;
                user.SsoSubject = identity.Subject;
                _logger.LogInformation("SSO 自动绑定：{Provider}:{Subject} -> {Username}",
                    provider.Id, identity.Subject, user.Username);
            }
        }

        if (user is null && options.AutoProvision)
        {
            user = await ProvisionAsync(provider.Id, identity, ct);
            _logger.LogInformation("SSO 自动开通账号：{Provider}:{Subject} -> {Username}",
                provider.Id, identity.Subject, user.Username);
        }

        if (user is null)
        {
            _logger.LogInformation("SSO 拒绝未绑定用户：{Provider}:{Subject}", provider.Id, identity.Subject);
            return (null, SsoLoginFailure.NotLinked);
        }

        if (!user.IsActive)
            return (null, SsoLoginFailure.NotLinked);

        // 与密码登录一致：记录登录信息、清会话缓存、签发含权限位图的 JWT
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ip;
        await _db.SaveChangesAsync(ct);
        _authService.InvalidateSessionCache(user.Id);

        return (await _authService.IssueForAsync(user, ct), null);
    }

    /// <summary>把企业身份绑定到已登录用户（个人中心「账号绑定」入口）。</summary>
    public async Task<(bool Ok, string Message)> BindIdentityAsync(
        string providerId, string code, string state, Guid currentUserId, CancellationToken ct)
    {
        var options = await GetEffectiveOptionsAsync(ct);
        var provider = ResolveProvider(providerId, options);
        if (provider is null)
            return (false, "该登录方式未启用");

        // 绑定回调也必须携带 authorize 发出的一次性 state（mode=bind），防止伪造回调直接绑号
        if (!TryConsumeState(state, providerId, out var mode) || mode != "bind")
            return (false, "绑定状态已过期，请重新发起绑定");

        var identity = await provider.ExchangeAsync(code, ct);
        if (identity is null || string.IsNullOrWhiteSpace(identity.Subject))
            return (false, "授权码无效或已过期，请重新扫码");

        var taken = await _db.Users.AsNoTracking().AnyAsync(
            u => u.SsoProvider == provider.Id && u.SsoSubject == identity.Subject && u.Id != currentUserId, ct);
        if (taken)
            return (false, "该企业身份已绑定到其他账号");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, ct);
        if (user is null)
            return (false, "当前用户不存在");

        user.SsoProvider = provider.Id;
        user.SsoSubject = identity.Subject;
        await _db.SaveChangesAsync(ct);
        return (true, "绑定成功");
    }

    private async Task<User> ProvisionAsync(string providerId, SsoIdentity identity, CancellationToken ct)
    {
        var username = $"{providerId}_{identity.Subject}";
        // 用户名撞库（比如 mock 用了和本地账号同名）：追加随机后缀保证唯一
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username, ct))
            username = $"{username}_{Guid.NewGuid():N}"[..44];
        if (username.Length > 50)
            username = username[..50];

        var displayName = identity.DisplayName ?? identity.Subject;
        if (displayName.Length > 100)
            displayName = displayName[..100];

        var user = new User
        {
            Username = username,
            // SSO 账号不用密码登录：随机不可用口令
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            DisplayName = displayName,
            Email = identity.Email,
            Role = UserRole.Viewer,
            IsActive = true,
            SsoProvider = providerId,
            SsoSubject = identity.Subject,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
