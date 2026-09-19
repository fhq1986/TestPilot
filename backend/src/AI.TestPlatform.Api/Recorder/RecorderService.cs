using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.Scripts;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Recorder;

/// <summary>
/// 录制器：驱动 Playwright 自带的 <c>codegen</c> 生成脚本，并把脚本实时解析成平台步骤。
///
/// 为什么复用 codegen 而不是自己写事件注入：
/// codegen 已经解决了「选择器优先级」「iframe 穿透」「等待策略」这些最难的部分，
/// 它产出的就是 Playwright 官方推荐的可读脚本；我们只需要在它写文件之后把文本
/// 交给 <see cref="PlaywrightScriptParser"/>——同一条解析链路同时服务"粘贴脚本导入"，
/// 因此录制出来的用例和手工导入的用例在步骤模型上完全一致。
///
/// 进程与文件都落在本实例上，所以录制会话是**单实例绑定**的交互会话。
/// </summary>
public class RecorderService
{
    // 进程句柄不能只靠数据库：DB 里的 PID 跨重启即失效，进程表才是判活的真身
    private static readonly ConcurrentDictionary<Guid, IRecorderProcess> Processes = new();

    private readonly TestDbContext _db;
    private readonly RecorderOptions _options;
    private readonly ILogger<RecorderService> _logger;
    private readonly IRecorderProcessLauncher _launcher;
    private readonly string _root;

    public RecorderService(TestDbContext db, IOptions<RecorderOptions> options,
        IHostEnvironment environment, IRecorderProcessLauncher launcher,
        ILogger<RecorderService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
        _launcher = launcher;
        _root = Path.GetFullPath(_options.Path, environment.ContentRootPath);
    }

    public string Root => _root;

    /// <summary>Playwright 自带的 node 可执行文件（按平台目录探测）</summary>
    private static string? ResolveNode()
    {
        var baseDir = AppContext.BaseDirectory;
        var nodeRoot = Path.Combine(baseDir, ".playwright", "node");
        if (!Directory.Exists(nodeRoot)) return null;

        // 目录名按平台变化（win32_x64 / linux / darwin-arm64 …），逐个探测而不是硬编码
        foreach (var dir in Directory.EnumerateDirectories(nodeRoot))
        {
            foreach (var exe in new[] { "node.exe", "node" })
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Playwright 的 CLI 入口（codegen 命令由它提供）</summary>
    private static string? ResolveCli()
    {
        var cli = Path.Combine(AppContext.BaseDirectory, ".playwright", "package", "cli.js");
        return File.Exists(cli) ? cli : null;
    }

    /// <summary>环境是否具备录制能力（缺 node/cli 时前端应给出明确提示而不是报 500）</summary>
    public static (bool Ok, string? Reason) CheckAvailability()
    {
        if (ResolveNode() is null)
            return (false, "未找到 Playwright 自带的 Node 运行时（.playwright/node），请先执行 dotnet build 还原包内容");
        if (ResolveCli() is null)
            return (false, "未找到 Playwright CLI（.playwright/package/cli.js）");
        return (true, null);
    }

    public async Task<RecorderSession> GetAsync(Guid id, CancellationToken ct) =>
        await _db.RecorderSessions.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new RecorderNotFoundException(id);

    public async Task<List<RecorderSession>> ListAsync(Guid? projectId, CancellationToken ct)
    {
        var query = _db.RecorderSessions.AsNoTracking().AsQueryable();
        if (projectId is not null) query = query.Where(s => s.ProjectId == projectId);
        return await query.OrderByDescending(s => s.CreatedAt).Take(100).ToListAsync(ct);
    }

    /// <summary>创建会话并拉起浏览器（codegen 会立即写出含初始 goto 的脚本文件）</summary>
    public async Task<RecorderSession> StartAsync(Guid projectId, string name, string? baseUrl,
        string? browser, Guid? userId, string? userName, CancellationToken ct)
    {
        if (!_options.Enabled)
            throw new RecorderUnavailableException("录制功能已在服务端关闭（Recorder:Enabled = false）");

        var (available, reason) = CheckAvailability();
        if (!available) throw new RecorderUnavailableException(reason!);

        // 只统计真正占着浏览器的会话；Stopped/Failed/Idle 不占用额度
        var active = await _db.RecorderSessions
            .CountAsync(s => RecorderSessionState.ActiveStatuses.Contains(s.Status), ct);
        if (active >= Math.Max(1, _options.MaxConcurrentSessions))
            throw new RecorderUnavailableException(
                $"同时进行的录制会话已达上限（{_options.MaxConcurrentSessions} 个），请先停止其它录制");

        var engine = BrowserCatalog.Normalize(browser);
        if (!BrowserCatalog.IsRecognized(browser))
            throw new RecorderUnavailableException($"无法识别的浏览器「{browser}」");

        var session = new RecorderSession
        {
            ProjectId = projectId,
            Name = string.IsNullOrWhiteSpace(name) ? "未命名录制" : name.Trim(),
            BaseUrl = NormalizeUrl(baseUrl),
            Browser = engine,
            Status = RecorderStatus.Idle,
            CreatedById = userId,
            CreatedByName = userName,
            CreatedAt = DateTime.UtcNow,
        };

        Directory.CreateDirectory(_root);

        _db.RecorderSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        // 路径在 SaveChanges 之后再拼：此刻主键一定已就绪。
        // （实体本身也显式初始化了 Id，两道保险——路径撞车会让不同会话互相覆盖脚本，且是静默的）
        session.OutputPath = Path.Combine(_root, $"{session.Id:N}.ts");
        await _db.SaveChangesAsync(ct);

        try
        {
            LaunchCodegen(session);
            session.Status = RecorderStatus.Recording;
            session.StartedAt = DateTime.UtcNow;
            session.LastPolledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            session.Status = RecorderStatus.Failed;
            session.LastError = RecorderSessionState.Shorten(ex.Message);
            session.StoppedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "拉起录制浏览器失败，会话 {SessionId}", session.Id);
            throw new RecorderUnavailableException($"拉起浏览器失败：{ex.Message}");
        }

        return session;
    }

    private void LaunchCodegen(RecorderSession session)
    {
        var node = ResolveNode()!;
        var cli = ResolveCli()!;

        var spec = new RecorderLaunchSpec(
            node, cli, session.OutputPath, session.Browser,
            _options.ViewportSize, session.BaseUrl);

        var process = _launcher.Launch(spec,
            line => _logger.LogDebug("[codegen] {Line}", line),
            line => _logger.LogDebug("[codegen:err] {Line}", line));

        process.Exited += (_, _) =>
        {
            // codegen 正常退出（用户关闭录制窗口）→ 按脚本是否已生成落状态，文件保留
            Processes.TryRemove(session.Id, out _);
            try
            {
                session.Status = RecorderSessionState.ResolveAfterProcessGone(
                    File.Exists(session.OutputPath));
                if (session.Status == RecorderStatus.Failed)
                    session.LastError ??= "浏览器未生成脚本就退出了";
                session.StoppedAt ??= DateTime.UtcNow;
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "录制会话 {SessionId} 退出时写状态失败", session.Id);
            }
        };

        Processes[session.Id] = process;
        session.ProcessId = process.Id;
    }

    /// <summary>
    /// 读取当前脚本并解析成步骤。这是前端轮询的主要入口。
    /// 返回的 <c>Changed</c> 为 false 时前端可直接复用上次结果，避免无谓的重渲染。
    /// </summary>
    public async Task<RecorderSnapshot> SnapshotAsync(Guid id, CancellationToken ct)
    {
        var session = await GetAsync(id, ct);
        RefreshStatus(session);

        session.LastPolledAt = DateTime.UtcNow;

        var script = ReadScript(session.OutputPath);
        var fingerprint = Fingerprint(script);
        var changed = fingerprint != session.ScriptFingerprint;

        ScriptParseResult? parsed = null;
        if (script is not null)
        {
            parsed = PlaywrightScriptParser.Parse(script);
            if (changed)
            {
                session.ScriptFingerprint = fingerprint;
                session.StepCount = parsed.Steps.Count;
                if (session.SavedTestCaseId is null && session.Name == "未命名录制" && parsed.SuggestedName is not null)
                {
                    // codegen 默认标题是 "test"，没有信息量；只在用户没改名且推断名有意义时采用
                    if (!string.Equals(parsed.SuggestedName, "test", StringComparison.OrdinalIgnoreCase))
                        session.Name = parsed.SuggestedName;
                }
                session.BaseUrl ??= parsed.SuggestedBaseUrl;
            }
        }

        await _db.SaveChangesAsync(ct);

        return new RecorderSnapshot(
            session.Id, session.Status, changed, script ?? string.Empty,
            session.StepCount, parsed?.Steps ?? [], parsed?.Warnings ?? [],
            session.BaseUrl, session.LastError, session.SavedTestCaseId, session.SavedTestCaseName);
    }

    /// <summary>停止录制：结束 codegen 进程树（连带关闭浏览器窗口）</summary>
    public async Task<RecorderSession> StopAsync(Guid id, CancellationToken ct)
    {
        var session = await GetAsync(id, ct);
        KillProcess(session);
        session.Status = RecorderStatus.Stopped;
        session.StoppedAt ??= DateTime.UtcNow;
        // 停止时再解析一次，确保 StepCount 是最终值
        var script = ReadScript(session.OutputPath);
        if (script is not null)
        {
            var parsed = PlaywrightScriptParser.Parse(script);
            session.StepCount = parsed.Steps.Count;
            session.ScriptFingerprint = Fingerprint(script);
        }
        await _db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>保存录制结果：解析最终脚本 → 创建用例（步骤模型与脚本导入完全一致）</summary>
    public async Task<(TestCase? TestCase, string? Error)> SaveAsync(Guid id, SaveRecorderRequest request,
        CancellationToken ct)
    {
        var session = await GetAsync(id, ct);
        if (session.SavedTestCaseId is not null)
            return (null, "该录制会话已经保存过，请新建会话重新录制");

        var script = ReadScript(session.OutputPath);
        if (string.IsNullOrWhiteSpace(script))
            return (null, "还没有捕获到任何操作，无法保存");

        var parsed = PlaywrightScriptParser.Parse(script);
        if (!parsed.Ok)
            return (null, parsed.Error ?? "脚本解析失败");
        if (parsed.Steps.Count == 0)
            return (null, "脚本中没有可识别的步骤，请至少操作一次页面");

        var name = string.IsNullOrWhiteSpace(request.Name) ? session.Name : request.Name.Trim();
        var testCase = new TestCase
        {
            ProjectId = session.ProjectId,
            Name = name.Length > 200 ? name[..200] : name,
            Type = parsed.SuggestedType,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Module = string.IsNullOrWhiteSpace(request.Module) ? null : request.Module.Trim(),
            Priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(),
            Browser = session.Browser,
            Timeout = 30000,
            Status = TestCaseStatus.Draft,
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? session.BaseUrl : request.BaseUrl.Trim(),
        };

        foreach (var step in parsed.Steps)
        {
            testCase.Steps.Add(new TestStep
            {
                StepOrder = step.StepOrder,
                ActionType = step.ActionType,
                Config = step.Config,
                AIInstruction = step.Instruction,
                AIElementDescription = step.Description,
            });
        }

        _db.TestCases.Add(testCase);
        session.SavedTestCaseId = testCase.Id;
        session.SavedTestCaseName = testCase.Name;
        session.Status = RecorderStatus.Stopped;
        session.StoppedAt ??= DateTime.UtcNow;
        KillProcess(session);

        await _db.SaveChangesAsync(ct);
        return (testCase, null);
    }

    /// <summary>删除会话（连带清理脚本临时文件）</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var session = await _db.RecorderSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null) return false;

        KillProcess(session);
        TryDeleteFile(session.OutputPath);

        _db.RecorderSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// 回收废弃会话：进程已死 或 长时间无人轮询。
    /// 与执行队列的 ReapStaleAsync 同思路——**主动回收而不是等下次请求发现**，
    /// 否则用户关掉页面后浏览器窗口会一直留在桌面上。
    /// </summary>
    public async Task<int> ReapStaleAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var threshold = now.AddMinutes(-Math.Max(1, _options.IdleTimeoutMinutes));
        var stale = await _db.RecorderSessions
            .Where(s => RecorderSessionState.ActiveStatuses.Contains(s.Status))
            .Where(s => s.LastPolledAt == null || s.LastPolledAt < threshold)
            .Take(50)
            .ToListAsync(ct);

        var reaped = 0;
        foreach (var session in stale)
        {
            // 双重校验：查询用的是 SQL 可翻译的近似条件，最终以纯函数判定为准
            if (!RecorderSessionState.IsIdleTimeout(session.LastPolledAt, now, _options.IdleTimeoutMinutes))
                continue;
            KillProcess(session);
            session.Status = RecorderStatus.Stopped;
            session.StoppedAt ??= now;
            session.LastError ??= "会话长时间无操作，已自动停止";
            reaped++;
        }

        // 另外处理"数据库说在录、进程已经没了"的情况（进程被外部结束、实例重启）
        var running = await _db.RecorderSessions
            .Where(s => s.Status == RecorderStatus.Recording)
            .ToListAsync(ct);
        foreach (var session in running)
        {
            if (Processes.ContainsKey(session.Id)) continue;
            session.Status = RecorderSessionState.ResolveAfterProcessGone(
                File.Exists(session.OutputPath));
            session.StoppedAt ??= now;
            session.LastError ??= "录制进程已结束";
            reaped++;
        }

        // 清理过期会话（含脚本文件）
        var expiryCutoff = now.AddDays(-Math.Max(1, _options.RetentionDays));
        var expired = await _db.RecorderSessions
            .Where(s => s.Status != RecorderStatus.Recording && s.CreatedAt < expiryCutoff)
            .Take(200)
            .ToListAsync(ct);
        foreach (var session in expired)
        {
            TryDeleteFile(session.OutputPath);
            _db.RecorderSessions.Remove(session);
        }

        if (reaped > 0 || expired.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("录制维护：回收会话 {Reaped} 个、清理过期 {Expired} 个", reaped, expired.Count);
        }
        return reaped;
    }

    /// <summary>把数据库状态与真实进程对齐（进程已退出但状态还是 Recording 时纠正）</summary>
    private void RefreshStatus(RecorderSession session)
    {
        if (!RecorderSessionState.IsActive(session.Status)) return;
        if (Processes.TryGetValue(session.Id, out var process) && !process.HasExited) return;

        Processes.TryRemove(session.Id, out _);
        session.Status = RecorderSessionState.ResolveAfterProcessGone(
            File.Exists(session.OutputPath));
        session.StoppedAt ??= DateTime.UtcNow;
        session.LastError ??= "录制窗口已关闭";
    }

    private void KillProcess(RecorderSession session)
    {
        if (!Processes.TryRemove(session.Id, out var process)) return;
        try
        {
            process.KillTree();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "结束录制进程失败，会话 {SessionId}", session.Id);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>读取脚本文件；codegen 可能正在写，用共享读写打开并容忍瞬时失败</summary>
    private string? ReadScript(string path)
    {
        if (!File.Exists(path)) return null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(30);
            }
        }
        return null;
    }

    private static string? Fingerprint(string? script)
    {
        if (script is null) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(script));
        return $"{script.Length}:{Convert.ToHexString(hash.AsSpan(0, 8))}";
    }

    private void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var full = Path.GetFullPath(path);
            // 只删自己目录下的文件，避免路径被污染后误删
            if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 归一化起始地址：只允许 http/https，缺协议时补 https。
    /// 返回值会被当作 codegen 的位置参数，因此必须挡掉 file:// 、--flag 之类的内容。
    /// </summary>
    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();
        if (trimmed.StartsWith("-", StringComparison.Ordinal)) return null;
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

}

/// <summary>一次轮询的完整快照（脚本原文 + 解析后的步骤 + 告警）</summary>
public record RecorderSnapshot(
    Guid SessionId,
    RecorderStatus Status,
    bool Changed,
    string Script,
    int StepCount,
    IReadOnlyList<ParsedStepDto> Steps,
    IReadOnlyList<string> Warnings,
    string? BaseUrl,
    string? LastError,
    Guid? SavedTestCaseId,
    string? SavedTestCaseName);

public record SaveRecorderRequest(
    string? Name = null, string? Module = null,
    string? Priority = null, string? BaseUrl = null, string? Description = null);

public class RecorderNotFoundException(Guid id) : Exception($"录制会话不存在：{id}");

public class RecorderUnavailableException(string message) : Exception(message);
