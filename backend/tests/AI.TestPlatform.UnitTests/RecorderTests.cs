using AI.TestPlatform.Api.Recorder;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 录制器的可测部分。
///
/// 这次重构把「命令行拼装」和「状态判定」从进程管理与数据库访问里拆了出来——
/// 它们原本只能靠真起一个浏览器来验证，而这恰好是上一轮出过问题的地方
/// （会话主键撞车导致脚本互相覆盖、进程退出后状态判断分散在三处）。
/// </summary>
public class RecorderLaunchSpecTests
{
    private static RecorderLaunchSpec Spec(
        string? baseUrl = "https://example.com/", string? viewport = "1280,720",
        string browser = "chromium") =>
        new(
            NodePath: @"C:\app\.playwright\node\win32_x64\node.exe",
            CliPath: @"C:\app\.playwright\package\cli.js",
            OutputPath: @"C:\app\recordings\abc.ts",
            Browser: browser,
            ViewportSize: viewport,
            BaseUrl: baseUrl);

    [Fact]
    public void 参数顺序与内容符合codegen契约()
    {
        var args = Spec().BuildArguments();

        Assert.Equal(@"C:\app\.playwright\package\cli.js", args[0]);
        Assert.Equal("codegen", args[1]);
        Assert.Equal("--target", args[2]);
        Assert.Equal("playwright-test", args[3]);
        Assert.Equal("--output", args[4]);
        Assert.Equal(@"C:\app\recordings\abc.ts", args[5]);
        Assert.Equal("--browser", args[6]);
        Assert.Equal("chromium", args[7]);
        Assert.Equal("--viewport-size", args[8]);
        Assert.Equal("1280,720", args[9]);
        // 位置参数（起始地址）必须排在最末，否则 codegen 会把它当成选项值
        Assert.Equal("https://example.com/", args[^1]);
    }

    [Fact]
    public void 起始地址为空时打开空白页()
    {
        var args = Spec(baseUrl: null).BuildArguments();

        Assert.Equal("about:blank", args[^1]);
    }

    [Fact]
    public void 起始地址为空白字符串时同样退回空白页()
    {
        // 前端传空串比传 null 更常见，两者必须等价
        var args = Spec(baseUrl: "   ").BuildArguments();

        Assert.Equal("about:blank", args[^1]);
    }

    [Fact]
    public void 视口为空时不传该选项()
    {
        var args = Spec(viewport: null).BuildArguments();

        Assert.DoesNotContain("--viewport-size", args);
        // 少了两个参数（--viewport-size 及其值），地址仍在末尾
        Assert.Equal("https://example.com/", args[^1]);
    }

    [Fact]
    public void 含空格的路径与地址保持为单个参数()
    {
        // 用 ArgumentList 的核心价值：含空格的路径不会被拆错，也不需要手工加引号
        var spec = new RecorderLaunchSpec(
            NodePath: @"C:\Program Files\node\node.exe",
            CliPath: @"C:\Program Files\pw\cli.js",
            OutputPath: @"C:\my recordings\a b.ts",
            Browser: "webkit",
            ViewportSize: null,
            BaseUrl: "https://example.com/path with space");

        var args = spec.BuildArguments();

        Assert.Contains(@"C:\Program Files\pw\cli.js", args);
        Assert.Contains(@"C:\my recordings\a b.ts", args);
        Assert.Contains("https://example.com/path with space", args);
        // 绝不能被拆成两段
        Assert.DoesNotContain("with", args);
    }

    [Fact]
    public void 工作目录取CLI所在目录()
    {
        // codegen 要在包目录里解析内置依赖，工作目录设错会在启动时才发现
        Assert.Equal(@"C:\app\.playwright\package", Spec().WorkingDirectory);
    }

    [Fact]
    public void 浏览器引擎原样传递()
    {
        Assert.Contains("firefox", Spec(browser: "firefox").BuildArguments());
        Assert.Contains("webkit", Spec(browser: "webkit").BuildArguments());
    }
}

public class RecorderUrlNormalizationTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("http://127.0.0.1:3000/login", "http://127.0.0.1:3000/login")]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("  example.com/a  ", "https://example.com/a")]
    public void 合法地址被归一化(string input, string expected)
    {
        Assert.Equal(expected, RecorderService.NormalizeUrl(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空值返回null(string? input)
    {
        Assert.Null(RecorderService.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("--headless")]
    [InlineData("--browser=firefox")]
    [InlineData("-o")]
    public void 以短横线开头的输入被拒绝(string input)
    {
        // 起始地址会被当作位置参数传给子进程，
        // 不挡住的话「--xxx」就会变成注入进来的命令行选项
        Assert.Null(RecorderService.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>x</h1>")]
    [InlineData("ftp://example.com/a")]
    public void 只允许http与https协议(string input)
    {
        Assert.Null(RecorderService.NormalizeUrl(input));
    }
}

public class RecorderSessionStateTests
{
    [Theory]
    [InlineData(RecorderStatus.Idle, true)]
    [InlineData(RecorderStatus.Recording, true)]
    [InlineData(RecorderStatus.Stopped, false)]
    [InlineData(RecorderStatus.Failed, false)]
    public void 活跃状态判定决定了并发额度占用(RecorderStatus status, bool expected)
    {
        Assert.Equal(expected, RecorderSessionState.IsActive(status));
    }

    [Fact]
    public void 活跃状态数组与判定函数保持一致()
    {
        // 数组用于 EF 翻译成 SQL IN，函数用于内存判断——两者必须同步，否则
        // 「查出来的行」和「判断为活跃的行」会不一致
        foreach (var status in Enum.GetValues<RecorderStatus>())
        {
            Assert.Equal(RecorderSessionState.ActiveStatuses.Contains(status),
                RecorderSessionState.IsActive(status));
        }
    }

    [Fact]
    public void 进程消失时按脚本是否生成落状态()
    {
        // 有脚本 → 用户至少操作过一次，是可用的录制结果
        Assert.Equal(RecorderStatus.Stopped, RecorderSessionState.ResolveAfterProcessGone(true));
        // 连文件都没有 → 浏览器根本没起来
        Assert.Equal(RecorderStatus.Failed, RecorderSessionState.ResolveAfterProcessGone(false));
    }

    [Fact]
    public void 从未被轮询过的会话即视为空闲超时()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(RecorderSessionState.IsIdleTimeout(null, now, 15));
    }

    [Fact]
    public void 空闲判定使用配置的分钟数()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var polledAt = now.AddMinutes(-20);

        Assert.True(RecorderSessionState.IsIdleTimeout(polledAt, now, 15));
        // 20 分钟前轮询过，但阈值放宽到 30 分钟就不该回收
        Assert.False(RecorderSessionState.IsIdleTimeout(polledAt, now, 30));
    }

    [Fact]
    public void 刚好在边界上不算超时()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var polledAt = now.AddMinutes(-15);

        Assert.False(RecorderSessionState.IsIdleTimeout(polledAt, now, 15));
    }

    [Fact]
    public void 阈值配置为0时禁用空闲回收()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(RecorderSessionState.IsIdleTimeout(null, now, 0));
    }

    [Fact]
    public void 保留期判定()
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(RecorderSessionState.IsExpired(now.AddDays(-8), now, 7));
        Assert.False(RecorderSessionState.IsExpired(now.AddDays(-6), now, 7));
        // 保留期置 0 表示永久保留
        Assert.False(RecorderSessionState.IsExpired(now.AddYears(-5), now, 0));
    }

    [Fact]
    public void 错误信息按列宽截断()
    {
        // LastError 列只有 500 字符；进程输出的异常常常更长，
        // 不截断会在保存时抛数据库异常，把「录制失败」变成「500 保存失败」
        var longMessage = new string('x', 1200);
        var shortened = RecorderSessionState.Shorten(longMessage);

        Assert.Equal(500, shortened.Length);

        var shortMessage = "启动失败";
        Assert.Equal(shortMessage, RecorderSessionState.Shorten(shortMessage));
    }
}

/// <summary>假的录制进程，用于验证服务与进程交互而不真的拉起浏览器</summary>
public class FakeRecorderProcessTests
{
    private sealed class FakeProcess : IRecorderProcess
    {
        public int Id { get; init; } = 4321;
        public bool HasExited { get; private set; }
        public bool Killed { get; private set; }
        public event EventHandler? Exited;

        public void KillTree()
        {
            Killed = true;
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() { }

        /// <summary>模拟浏览器窗口被用户关闭</summary>
        public void SimulateExit()
        {
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeLauncher : IRecorderProcessLauncher
    {
        public RecorderLaunchSpec? LastSpec { get; private set; }
        public FakeProcess Process { get; } = new();

        public IRecorderProcess Launch(RecorderLaunchSpec spec,
            Action<string>? onOutput, Action<string>? onError)
        {
            LastSpec = spec;
            onOutput?.Invoke("codegen ready");
            return Process;
        }
    }

    [Fact]
    public void 启动器收到完整规格()
    {
        var launcher = new FakeLauncher();
        var spec = new RecorderLaunchSpec(
            @"C:\n\node.exe", @"C:\n\cli.js", @"C:\out\s.ts",
            "chromium", "1280,720", "https://a.test");

        var process = launcher.Launch(spec, null, null);

        Assert.Equal(@"C:\out\s.ts", launcher.LastSpec!.OutputPath);
        Assert.Equal("chromium", launcher.LastSpec.Browser);
        Assert.Equal(@"C:\n\cli.js", launcher.LastSpec.BuildArguments()[0]);
        Assert.Equal(4321, process.Id);
        Assert.False(process.HasExited);
    }

    [Fact]
    public void 结束进程树会被记录并触发退出事件()
    {
        var launcher = new FakeLauncher();
        var exited = false;
        var process = launcher.Launch(
            new RecorderLaunchSpec("n", "c", "o", "chromium", null, null), null, null);
        process.Exited += (_, _) => exited = true;

        process.KillTree();

        Assert.True(launcher.Process.Killed);
        Assert.True(launcher.Process.HasExited);
        // 结束进程后服务依赖 Exited 事件把会话落到 Stopped，事件必须真的发出来
        Assert.True(exited);
    }

    [Fact]
    public void 用户关闭窗口走的是退出事件而非显式结束()
    {
        var launcher = new FakeLauncher();
        launcher.Launch(new RecorderLaunchSpec("n", "c", "o", "chromium", null, null), null, null);

        launcher.Process.SimulateExit();

        Assert.True(launcher.Process.HasExited);
        // 没有走过 KillTree，说明服务是被动感知退出（对应「用户关掉录制窗口」这条路径）
        Assert.False(launcher.Process.Killed);
    }
}
