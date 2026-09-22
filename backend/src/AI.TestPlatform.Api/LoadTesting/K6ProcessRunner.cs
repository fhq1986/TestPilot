using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>一次 k6 运行的输入</summary>
public sealed record K6RunRequest(
    Guid RunId,
    string WorkDir,
    string ScriptPath,
    /// <summary>k6 --summary-export 的输出文件路径</summary>
    string SummaryExportPath,
    /// <summary>handleSummary 里 SUMMARY_PATH 指向的文件</summary>
    string SummaryPath,
    string BaseUrl,
    IReadOnlyDictionary<string, string> Variables,
    /// <summary>整体超时（秒）：场景时长 + 余量</summary>
    int TimeoutSeconds);

/// <summary>一次 k6 运行的结果</summary>
public sealed record K6RunOutcome(
    int? ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut,
    string? SummaryJson,
    string? K6Version,
    string? Error);

/// <summary>
/// 跑 k6 的接缝（迭代 F·P2-9）。
///
/// 抽成接口是为了将来把执行面换成独立 runner 服务时**只换实现**：
/// 现在是「k6 二进制打进 backend 镜像 + 子进程」，将来可以换成"投递到独立 runner"，
/// LoadTestWorker 的抢占/心跳/落库逻辑一行都不用动。
/// </summary>
public interface IK6ProcessRunner
{
    Task<K6RunOutcome> RunAsync(K6RunRequest request, CancellationToken ct);
}

/// <summary>
/// 以子进程方式跑 k6。
///
/// 为什么不用 <c>docker run grafana/k6</c>：那需要把宿主机的
/// <c>/var/run/docker.sock</c> 挂进后端容器，等于把宿主机 root 交给一个 Web 服务，
/// 与本项目「postgres/minio 只绑回环、生产缺配置直接启动失败」的安全姿态直接冲突。
/// </summary>
public sealed class K6ProcessRunner : IK6ProcessRunner
{
    private readonly LoadTestOptions _options;
    private readonly ILogger<K6ProcessRunner> _logger;

    /// <summary>k6 版本只在首次运行时探一次：它决定 summary 的结构，出问题时要有据可查</summary>
    private static string? _cachedVersion;

    public K6ProcessRunner(IOptions<LoadTestOptions> options, ILogger<K6ProcessRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<K6RunOutcome> RunAsync(K6RunRequest request, CancellationToken ct)
    {
        var version = await DetectVersionAsync(ct);

        var psi = new ProcessStartInfo
        {
            FileName = _options.K6Path,
            WorkingDirectory = request.WorkDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // nice 降优先级：压测与平台同机抢 CPU，不降的话正常请求会被拖慢
        if (_options.UseNice && !OperatingSystem.IsWindows())
        {
            psi.FileName = "nice";
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("10");
            psi.ArgumentList.Add(_options.K6Path);
        }

        // ArgumentList 会自行处理引号转义，不要手工拼字符串
        psi.ArgumentList.Add("run");
        // 默认会向 Grafana 发匿名使用统计；部署机网络受限，且没必要
        psi.ArgumentList.Add("--no-usage-report");
        psi.ArgumentList.Add("--no-color");
        psi.ArgumentList.Add($"--summary-export={request.SummaryExportPath}");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add($"BASE_URL={request.BaseUrl}");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add($"SUMMARY_PATH={request.SummaryPath}");
        foreach (var (key, value) in request.Variables)
        {
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"VAR_{key}={value}");
        }
        psi.ArgumentList.Add(request.ScriptPath);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        try
        {
            process.OutputDataReceived += (_, e) => Append(stdout, e.Data);
            process.ErrorDataReceived += (_, e) => Append(stderr, e.Data);

            if (!process.Start())
                return new K6RunOutcome(null, "", "", false, null, version, $"无法启动 k6：{_options.K6Path}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, request.TimeoutSeconds)));

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                KillTree(process);
                // 被取消（用户终止 / 超时）后要给 k6 一点时间落盘，但不能再无限等
                try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { /* 已经杀干净了 */ }
            }

            var summaryJson = await ReadIfExistsAsync(request.SummaryExportPath)
                              ?? await ReadIfExistsAsync(request.SummaryPath);

            var exitCode = timedOut ? (int?)null : SafeExitCode(process);

            return new K6RunOutcome(
                exitCode,
                stdout.ToString(),
                stderr.ToString(),
                timedOut,
                summaryJson,
                version,
                timedOut ? $"执行超时（{request.TimeoutSeconds}s）" : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "运行 k6 失败 runId={RunId}", request.RunId);
            return new K6RunOutcome(null, stdout.ToString(), stderr.ToString(), false, null, version, ex.Message);
        }
    }

    private async Task<string?> DetectVersionAsync(CancellationToken ct)
    {
        if (_cachedVersion is not null) return _cachedVersion;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _options.K6Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("version");

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            _cachedVersion = output.Trim();
            return _cachedVersion;
        }
        catch (Exception ex)
        {
            // 探测不到版本不该阻断运行：可能是 k6 未安装，真正的错误会在 RunAsync 里报出来
            _logger.LogWarning(ex, "探测 k6 版本失败（k6 可能未安装）");
            return null;
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return null; }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 进程可能刚好自己退出了；杀失败不是错误
        }
    }

    private static void Append(StringBuilder sb, string? line)
    {
        if (line is null) return;
        lock (sb) sb.AppendLine(line);
    }

    private static async Task<string?> ReadIfExistsAsync(string path)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
