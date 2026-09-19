using System.Diagnostics;

namespace AI.TestPlatform.Api.Recorder;

/// <summary>
/// 一次录制要交给 codegen 的全部参数。
///
/// 单独抽成记录类型而不是直接在服务里拼 <c>ProcessStartInfo</c>，
/// 是为了让「敲了什么命令」这件事可被单测断言——
/// 参数拼错（路径、浏览器、起始地址）在运行时表现为「浏览器起不来」，
/// 排查成本很高，必须在编译期/测试期就锁住。
/// </summary>
public record RecorderLaunchSpec(
    string NodePath,
    string CliPath,
    string OutputPath,
    string Browser,
    string? ViewportSize,
    string? BaseUrl)
{
    /// <summary>codegen 的工作目录（Playwright 包目录，便于它解析内置依赖）</summary>
    public string WorkingDirectory => Path.GetDirectoryName(CliPath) ?? AppContext.BaseDirectory;

    /// <summary>
    /// 构造 codegen 的参数列表。
    ///
    /// 用 <c>ArgumentList</c> 而不是拼字符串：路径含空格不会被拆错，也完全绕开 shell 注入。
    /// 这是必须用列表形态的核心理由——起始地址来自用户输入。
    /// </summary>
    public List<string> BuildArguments()
    {
        var args = new List<string>
        {
            CliPath,
            "codegen",
            "--target", "playwright-test",
            "--output", OutputPath,
            "--browser", Browser,
        };
        if (!string.IsNullOrWhiteSpace(ViewportSize))
        {
            args.Add("--viewport-size");
            args.Add(ViewportSize);
        }
        // 留空则打开空白页，由用户自行导航
        args.Add(string.IsNullOrWhiteSpace(BaseUrl) ? "about:blank" : BaseUrl);
        return args;
    }
}

/// <summary>录制进程的抽象，便于用假实现替换真实 codegen 进程</summary>
public interface IRecorderProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    event EventHandler Exited;
    /// <summary>结束进程树（node 下还挂着浏览器，只杀父进程会留下孤儿窗口）</summary>
    void KillTree();
}

/// <summary>录制进程启动器抽象（真实实现用 <see cref="Process"/>，测试用假实现）</summary>
public interface IRecorderProcessLauncher
{
    /// <summary>按规格拉起 codegen。失败抛异常，由调用方转成会话的 Failed 状态</summary>
    IRecorderProcess Launch(RecorderLaunchSpec spec, Action<string>? onOutput, Action<string>? onError);
}

/// <summary>基于 <see cref="Process"/> 的真实实现</summary>
public sealed class RecorderProcessLauncher : IRecorderProcessLauncher
{
    public IRecorderProcess Launch(RecorderLaunchSpec spec, Action<string>? onOutput, Action<string>? onError)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.NodePath,
            WorkingDirectory = spec.WorkingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            // 不走 shell：配合 ArgumentList 彻底消除注入面
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in spec.BuildArguments())
            startInfo.ArgumentList.Add(arg);

        // 宿主注入了 HTTP_PROXY 但没有 NO_PROXY 时，Playwright 会把 127.0.0.1 也走代理而失败
        // （曾导致本机回环调用 404 / 浏览器起不来），这里显式排除回环地址。
        startInfo.Environment["NO_PROXY"] = "127.0.0.1,localhost,::1";
        startInfo.Environment["no_proxy"] = "127.0.0.1,localhost,::1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start 返回空，无法启动 codegen");

        process.EnableRaisingEvents = true;

        // 不读走输出会把管道缓冲区填满从而卡住子进程
        if (onOutput is not null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
            process.BeginOutputReadLine();
        }
        if (onError is not null)
        {
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onError(e.Data); };
            process.BeginErrorReadLine();
        }

        return new RealRecorderProcess(process);
    }

    private sealed class RealRecorderProcess : IRecorderProcess
    {
        private readonly Process _process;

        public RealRecorderProcess(Process process)
        {
            _process = process;
            _process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        }

        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public event EventHandler? Exited;

        public void KillTree()
        {
            if (_process.HasExited) return;
            // entireProcessTree：node 进程下还挂着浏览器进程
            _process.Kill(entireProcessTree: true);
        }

        public void Dispose() => _process.Dispose();
    }
}
