using System.Diagnostics;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Visual;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.ApiTesting;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.SharedSteps;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Environment = AI.TestPlatform.Domain.Entities.Environment;

namespace AI.TestPlatform.Api.Execution;

public class TestRunner
{
    /// <summary>UploadFile 单个文件的大小上限。SetInputFiles 把文件整体读进内存，必须设上限</summary>
    internal const long MaxUploadFileBytes = 50 * 1024 * 1024;

    private readonly ScreenshotStorage _screenshots;
    private readonly AIClient _aiClient;
    private readonly ElementCacheService _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VisualRegressionService _visual;
    private readonly ILogger<TestRunner> _logger;
    private readonly ExecutionOptions _options;
    private readonly TestDbContext _db;
    private readonly BrowserPool _browsers;
    private readonly TraceStorage _trace;
    private readonly A11yScanner _a11y;
    private readonly VideoStorage _videos;
    private readonly AuthStateCache _authStates;

    public TestRunner(ScreenshotStorage screenshots, AIClient aiClient,
        ElementCacheService cache, IHttpClientFactory httpClientFactory,
        VisualRegressionService visual, TestDbContext db, BrowserPool browsers,
        TraceStorage trace, A11yScanner a11y, VideoStorage videos, AuthStateCache authStates,
        ILogger<TestRunner> logger, IOptions<ExecutionOptions> options)
    {
        _screenshots = screenshots;
        _aiClient = aiClient;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _visual = visual;
        _db = db;
        _browsers = browsers;
        _trace = trace;
        _a11y = a11y;
        _videos = videos;
        _authStates = authStates;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<List<ExecutionResult>> RunAsync(
        global::AI.TestPlatform.Domain.Entities.Execution execution,
        Environment? environment,
        CancellationToken ct,
        Func<ExecutionResult, Task>? onStepCompleted = null,
        Func<int, StepConfig?, Task>? onStepStarted = null)
    {
        var testCase = execution.TestCase!;
        // 数据驱动：把 {{变量}} 换成本次执行的值（未绑数据集/无变量时原样返回）
        var variables = execution.Variables;
        var steps = await ResolveStepsAsync(testCase, variables, ct);
        // 步骤总数快照（展开后、不含自动登录前置）：详情页「共 x 步」进度指示的分母。
        // 由步骤完成回调的增量 SaveChanges 尽早落库，整条结束后兜底落库。
        execution.TotalSteps = steps.Count;

        if (testCase.Type == TestType.Api)
            return await RunApiAsync(execution.Id, testCase, steps, environment, onStepCompleted, onStepStarted, ct);

        var results = new List<ExecutionResult>();

        // 浏览器矩阵：执行时已解析好具体引擎。浏览器实例来自池，**不要释放它**——
        // 每次执行只新建并释放自己的 context，冷启动开销就被摊掉了。
        var browserName = BrowserCatalog.Normalize(execution.BrowserName ?? environment?.Browser ?? testCase.Browser);
        execution.BrowserName = browserName;
        var (browser, browserVersion) = await _browsers.GetAsync(browserName, ct);
        execution.BrowserVersion = browserVersion;

        // ---- 自动登录：环境开启且账号密码齐全时，步骤执行前先登录
        var autoLoginEnabled = environment is { AutoLogin: true }
            && !string.IsNullOrWhiteSpace(environment.LoginUsername)
            && !string.IsNullOrWhiteSpace(environment.LoginPassword);

        // 登录态复用：命中缓存就把 storageState 直接装进上下文，本次执行**完全不用登录**。
        // 这是纯收益——省掉每条用例一次登录的墙钟时间，也少一个"登录接口抖一下、整批用例一起变红"的源。
        var authStateKey = autoLoginEnabled ? AuthStateCache.KeyOf(environment, browserName) : null;
        var cachedAuthState = authStateKey is null ? null : _authStates.TryGet(authStateKey);

        // ---- 视频录制：先录到临时目录，失败才搬到产物存储（与 trace 同一套取舍）
        var videoOn = VideoStorage.IsEnabled(_options, out var videoMode);
        var videoDir = videoOn ? _videos.TempDirFor(execution.Id) : null;

        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = _options.ViewportWidth,
                Height = _options.ViewportHeight,
            },
            // 命中缓存的登录态时直接注入；未命中则由本次登录后写入缓存，下次生效
            StorageState = cachedAuthState,
        };
        if (videoDir is not null)
        {
            // Playwright 只往真实存在的目录写文件，目录不存在会直接报错
            Directory.CreateDirectory(videoDir);
            // .NET 绑定把协议里的 recordVideo:{dir,size} 摊平成两个属性（没有嵌套的 RecordVideo 对象），
            // 所以这里是 RecordVideoDir / RecordVideoSize 而不是 RecordVideo
            contextOptions.RecordVideoDir = videoDir;
            contextOptions.RecordVideoSize = new()
            {
                Width = _options.ViewportWidth,
                Height = _options.ViewportHeight,
            };
        }

        // 必须 await using：浏览器长期存活后，泄漏的 context 会在同一个浏览器里不断累积
        await using var context = await browser.NewContextAsync(contextOptions);

        // trace 记录：失败时保留可回放包（DOM 快照 + 网络 + 控制台），成功时丢弃
        var traceMode = (_options.TraceMode ?? "on-failure").Trim().ToLowerInvariant();
        var tracing = _options.TraceEnabled && traceMode != "off" && traceMode != "none";
        if (tracing)
        {
            try
            {
                await context.Tracing.StartAsync(new TracingStartOptions
                {
                    Screenshots = true,
                    // DOM 快照是 trace 最有价值的部分（能看到点击那一刻页面长什么样）
                    Snapshots = true,
                    // 不做源码关联：平台步骤不是 TS 源码，带上只会让包里多出无意义文件
                    Sources = false,
                });
            }
            catch (Exception ex)
            {
                // trace 是增强能力，起不来不能拖垮执行本身
                tracing = false;
                _logger.LogWarning(ex, "启动 trace 记录失败，本次执行不记录 trace");
            }
        }

        var page = await context.NewPageAsync();
        try
        {
            // 用例级网络拦截：必须排在自动登录与所有步骤**之前**注册——
            // route 只对注册之后发出的请求生效，晚一步那个请求就真的打出去了。
            await ApplyNetworkRulesAsync(page, testCase);

            // 自动登录前置：展示为 StepOrder=-1 的前置结果。
            // 命中登录态缓存时**不真的登录**，但仍补一条前置结果——
            // 否则步骤列表里会凭空少一行，用户会以为"前置步骤被吞了"。
            if (autoLoginEnabled)
            {
                // 先算出完整登录 URL（两处复用 + 日志）
                var rawLoginUrl = environment.LoginUrl ?? environment.BaseUrl;
                var fullLoginUrl = ResolveAbsoluteGotoUrl(rawLoginUrl, environment.BaseUrl, "自动登录地址");
                var stepConfig = new StepConfig { Url = fullLoginUrl };

                await TryNotifyStartedAsync(onStepStarted, -1, stepConfig);

                if (cachedAuthState is not null)
                {
                    // 缓存命中：storageState 已在 BrowserContext 创建时注入，
                    // 但 SPA 里 token 可能需要页面真的加载一次才能写入 / 生效。
                    // 先 Goto 登录页 + 硬等 URL 真的离开 login，确保 cookie 有效。
                    ExecutionResult reused;
                    try
                    {
                        await page.GotoAsync(fullLoginUrl, new PageGotoOptions
                        {
                            Timeout = testCase.Timeout,
                            WaitUntil = WaitUntilState.NetworkIdle,
                        });

                        // 硬等 URL 真的不在 login（15s）
                        var urlLeftLogin = await page.EvaluateAsync<bool>(
                            @"async () => {
                                const start = Date.now();
                                while (Date.now() - start < 15000) {
                                    if (!location.pathname.toLowerCase().includes('login')) return true;
                                    await new Promise(r => setTimeout(r, 150));
                                }
                                return false;
                            }");

                        _logger.LogWarning("缓存登录态验证: 点击后URL={Url}, 离开login={Left}", page.Url, urlLeftLogin);

                        reused = new ExecutionResult
                        {
                            ExecutionId = execution.Id,
                            StepOrder = -1,
                            StepSnapshot = stepConfig,
                            Status = ExecutionStatus.Passed,
                            Log = urlLeftLogin
                                ? $"复用缓存的登录态（未重复登录），验证通过"
                                : $"复用缓存的登录态，但 URL 仍在 \"{page.Url}\"（疑似登录态已过期）。不过继续执行后续步骤，路由守卫可能会自动跳转。",
                        };

                        // 缓存过期了但不想浪费这次机会：让后续步骤继续跑，
                        // 如果还会被踢回 /login，Navigate 的硬等 URL 会捕获并报错
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("缓存登录态验证失败: {Err}", ex.Message);
                        reused = new ExecutionResult
                        {
                            ExecutionId = execution.Id,
                            StepOrder = -1,
                            StepSnapshot = stepConfig,
                            Status = ExecutionStatus.Passed, // 仍判 Pass，让后续 Navigate 自己报错
                            Log = $"复用缓存的登录态，但验证失败（{ex.Message}）。继续执行后续步骤。",
                        };
                    }

                    results.Add(reused);
                    await TryNotifyCompletedAsync(onStepCompleted, reused);
                }
                else
                {
                    var loginResult = await TryAutoLoginAsync(page, environment, testCase, execution.Id, ct);
                    results.Add(loginResult);
                    await TryNotifyCompletedAsync(onStepCompleted, loginResult);
                    if (loginResult.Status != ExecutionStatus.Passed)
                        return results; // 登录失败：终止执行，仅保留登录失败结果

                    // 登录成功：抓住 storageState 供同环境的后续执行复用
                    if (authStateKey is not null)
                        await TryCacheAuthStateAsync(context, authStateKey, execution.Id);
                }
            }

            foreach (var step in steps)
            {
                // 手动终止：当前及剩余步骤补 Skipped 行并实时推送，不再执行
                if (ct.IsCancellationRequested)
                {
                    var cancelled = new ExecutionResult
                    {
                        ExecutionId = execution.Id,
                        TestStepId = PersistedStepId(step),
                        StepOrder = step.StepOrder,
                        StepSnapshot = CloneConfig(step.Config),
                        Status = ExecutionStatus.Skipped,
                        Log = "执行已被手动终止，未执行",
                    };
                    results.Add(cancelled);
                    await TryNotifyCompletedAsync(onStepCompleted, cancelled);
                    continue;
                }

                var stepSnapshot = CloneConfig(step.Config);
                await TryNotifyStartedAsync(onStepStarted, step.StepOrder, stepSnapshot);
                var result = new ExecutionResult
                {
                    ExecutionId = execution.Id,
                    TestStepId = PersistedStepId(step),
                    StepOrder = step.StepOrder,
                    StepSnapshot = stepSnapshot,
                    StepActionType = (int)step.ActionType,
                    Status = ExecutionStatus.Running,
                };
                var sw = Stopwatch.StartNew();
                try
                {
                    var (log, retriesUsed) = await ExecuteStepWithRetryAsync(page, step, testCase, environment, ct);
                    result.Log = retriesUsed > 0
                        ? log is null
                            ? $"步骤内部重试 {retriesUsed} 次后成功"
                            : $"{log}（内部重试 {retriesUsed} 次后成功）"
                        : log;
                    execution.StepRetryCount += retriesUsed;
                    result.Status = ExecutionStatus.Passed;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // 手动终止打断的步骤：单独标记，整条执行的终态由执行器定为 Canceled
                    result.Status = ExecutionStatus.Canceled;
                    result.ErrorMessage = "执行已被手动终止";
                }
                catch (Exception ex)
                {
                    result.Status = ExecutionStatus.Failed;
                    result.ErrorMessage = ex.Message;
                    result.StackTrace = ex.StackTrace;
                    _logger.LogWarning(ex, "步骤 {Order} 执行失败", step.StepOrder);
                }
                sw.Stop();
                result.DurationMs = (int)sw.ElapsedMilliseconds;

                try
                {
                    result.ScreenshotUrl = await _screenshots.SaveAsync(execution.Id, step.StepOrder, page, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "截图失败 步骤 {Order}", step.StepOrder);
                }

                // 视觉回归：开启后与基线比对（判定为变化会把步骤标记为失败）
                if (testCase.VisualEnabled && result.ScreenshotUrl is not null)
                    await _visual.ApplyAsync(execution, testCase, step, result, ct);

                results.Add(result);
                // 先回调再处理 FailFast：失败那一步也要实时出现在页面上（原来 break 会跳过回调，
                // 失败步骤只能等整条执行结束落库后才可见）
                await TryNotifyCompletedAsync(onStepCompleted, result);

                // 失败即中止：跳过剩余步骤并标记 Skipped
                if (testCase.FailFast && result.Status == ExecutionStatus.Failed)
                {
                    foreach (var remaining in steps.Where(s => s.StepOrder > step.StepOrder))
                    {
                        var skipped = new ExecutionResult
                        {
                            ExecutionId = execution.Id,
                            TestStepId = PersistedStepId(remaining),
                            StepOrder = remaining.StepOrder,
                            StepSnapshot = CloneConfig(remaining.Config),
                            StepActionType = (int)remaining.ActionType,
                            Status = ExecutionStatus.Skipped,
                            Log = "失败即中止（FailFast），未执行",
                        };
                        results.Add(skipped);
                        await TryNotifyCompletedAsync(onStepCompleted, skipped);
                    }
                    break;
                }
            }

            return results;
        }
        finally
        {
            // 放在 finally：登录失败提前 return、取消、未捕获异常都要把 trace 收尾，
            // 否则会留下一个永远写不完的 trace 记录
            if (tracing)
                await FinalizeTraceAsync(context, execution, results, traceMode);

            // 录像收尾必须排在 trace 之后：trace 要求 context 还活着，
            // 而视频文件要等 context 关闭才写完（见 FinalizeVideoAsync 内部注释）
            if (videoDir is not null)
                await FinalizeVideoAsync(context, page, execution, results, videoMode, videoDir);

            // 用了缓存登录态却失败了 → 十有八九是登录态已过期。作废它，让下次老老实实重新登录。
            // 没有这一步，一次过期会让后续每条用例都失败，而每条看起来都像"功能坏了"。
            if (cachedAuthState is not null && authStateKey is not null && HasFailure(results))
                _authStates.Invalidate(authStateKey);
        }
    }

    /// <summary>本次执行里是否出现了失败/错误（决定 trace 与录像是否保留）</summary>
    private static bool HasFailure(List<ExecutionResult> results) =>
        results.Any(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error);

    /// <summary>
    /// 登录成功后的 storageState 抓取。
    ///
    /// 抓不到不算错：复用登录态是增强能力，失败无非是"下次还照旧登录一次"，
    /// 绝不能因此把本次执行判失败。
    /// </summary>
    private async Task TryCacheAuthStateAsync(IBrowserContext context, string authStateKey, Guid executionId)
    {
        if (!_authStates.Enabled) return;
        try
        {
            var state = await context.StorageStateAsync();
            _authStates.Set(authStateKey, state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "抓取登录态失败，本次执行不缓存 执行 {ExecutionId}", executionId);
        }
    }

    /// <summary>
    /// 录像收尾：失败（或配置为 always）才把文件搬进产物存储，成功执行直接丢弃。
    ///
    /// ⚠ **必须先关闭 context**：Playwright 是在 context 关闭时才把录像写完整的，
    /// 提前读会拿到 0 字节或读不到文件。这里关掉之后，外层 `await using` 再关一次是无害的空操作。
    /// </summary>
    private async Task FinalizeVideoAsync(
        IBrowserContext context, IPage page, global::AI.TestPlatform.Domain.Entities.Execution execution,
        List<ExecutionResult> results, string videoMode, string videoDir)
    {
        try
        {
            await context.CloseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "关闭浏览器上下文时出错（无害）");
        }

        try
        {
            var keep = HasFailure(results) || videoMode == "always";
            if (!keep) return;

            var path = page.Video is null ? null : await page.Video.PathAsync();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            var size = await _videos.SaveFromAsync(path, execution.Id, _options.VideoMaxBytes);
            if (size is null) return;

            execution.VideoUrl = VideoStorage.Url(execution.Id);
            execution.VideoSizeBytes = size;
            _logger.LogInformation("已保留执行录像（{Size} 字节）执行 {ExecutionId}", size, execution.Id);
        }
        catch (Exception ex)
        {
            // 录像只是增强能力：存不上不能影响执行结果落库
            _logger.LogWarning(ex, "保存执行录像失败 执行 {ExecutionId}", execution.Id);
        }
        finally
        {
            // 无论保留还是丢弃都要清临时目录，否则会越积越多
            _videos.DeleteTemp(videoDir);
        }
    }

    /// <summary>
    /// trace 收尾：失败（或配置为 always）才把包留盘，成功执行直接丢弃。
    /// trace 带截图与 DOM 快照，单个几 MB，全量保留会迅速撑爆磁盘。
    /// </summary>
    private async Task FinalizeTraceAsync(
        IBrowserContext context, global::AI.TestPlatform.Domain.Entities.Execution execution,
        List<ExecutionResult> results, string traceMode)
    {
        var failed = results.Any(r =>
            r.Status is ExecutionStatus.Failed or ExecutionStatus.Error);
        var keep = failed || traceMode == "always";

        if (!keep)
        {
            await _trace.DiscardAsync(context.Tracing);
            return;
        }

        var size = await _trace.StopAndSaveAsync(context.Tracing, execution.Id);
        if (size is null) return;

        execution.TraceUrl = TraceStorage.Url(execution.Id);
        execution.TraceSizeBytes = size;
        _logger.LogInformation("已保留执行 trace（{Size} 字节）执行 {ExecutionId}",
            size, execution.Id);
    }

    /// <summary>
    /// 按执行变量解析全部步骤。
    ///
    /// 顺序很重要：**先展开共享步骤组，再做变量替换**。
    /// 反过来的话，共享步骤组里的 {{变量}} 会被外层数据集行提前消耗掉，
    /// 组内默认值就没机会生效了。
    /// </summary>
    private async Task<List<TestStep>> ResolveStepsAsync(
        TestCase testCase, IReadOnlyDictionary<string, string>? variables, CancellationToken ct)
    {
        var ordered = testCase.Steps.OrderBy(s => s.StepOrder).ToList();
        var referenced = SharedStepExpander.ReferencedGroupIds(ordered);

        if (referenced.Count == 0)
            return ordered.Select(s => ResolveStep(s, variables)).ToList();

        // 只加载真正被引用的组；被删掉的组不在结果里，由展开器记录告警并跳过
        var loaded = await _db.SharedStepGroups.AsNoTracking()
            .Include(g => g.Items)
            .Where(g => referenced.Contains(g.Id))
            .ToListAsync(ct);
        var groups = loaded.ToDictionary(g => g.Id);

        var warnings = new List<string>();
        var expanded = SharedStepExpander.Expand(ordered, groups, warnings);
        foreach (var warning in warnings)
            _logger.LogWarning("共享步骤展开告警（用例 {TestCaseId}）：{Warning}", testCase.Id, warning);

        return expanded.Select(s => ResolveStep(s, variables)).ToList();
    }

    /// <summary>
    /// 步骤对应的 TestSteps 主键，仅当它确实来自数据库时才有值。
    ///
    /// 共享步骤组展开出来的步骤是**内存里的临时对象**，Id 为空——直接写进
    /// ExecutionResults.TestStepId 会触发外键约束（指向 Guid.Empty），整个结果落库失败。
    /// 这类步骤写 null：结果的溯源信息本来就在 StepSnapshot 里，不缺这一列。
    /// </summary>
    private static Guid? PersistedStepId(TestStep step) =>
        step.Id == Guid.Empty ? null : step.Id;

    private static TestStep ResolveStep(TestStep step, IReadOnlyDictionary<string, string>? variables)
    {
        var config = StepVariableResolver.Resolve(step.Config, variables);
        if (config is null || ReferenceEquals(config, step.Config)) return step;
        return new TestStep
        {
            Id = step.Id,
            TestCaseId = step.TestCaseId,
            StepOrder = step.StepOrder,
            ActionType = step.ActionType,
            Config = config,
            AIInstruction = step.AIInstruction,
            AIElementDescription = step.AIElementDescription,
        };
    }

    private async Task<List<ExecutionResult>> RunApiAsync(
        Guid executionId, TestCase testCase, List<TestStep> steps, Environment? environment,
        Func<ExecutionResult, Task>? onStepCompleted,
        Func<int, StepConfig?, Task>? onStepStarted, CancellationToken ct)
    {
        var results = new List<ExecutionResult>();
        // IHttpClientFactory 创建客户端，避免每次执行 new HttpClient 导致 Socket 耗尽
        using var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromMilliseconds(Math.Max(testCase.Timeout, 1000));
        // 用例未单独配置 BaseUrl 时自动继承执行环境的地址，避免每个用例都要手填
        var effectiveBaseUrl = ResolveBaseUrl(testCase, environment);
        var executor = new ApiCaseExecutor(http, effectiveBaseUrl);
        ApiResponse? lastResponse = null;

        foreach (var step in steps)
        {
            // 手动终止：当前及剩余步骤补 Skipped 行并实时推送，不再执行
            if (ct.IsCancellationRequested)
            {
                var cancelled = new ExecutionResult
                {
                    ExecutionId = executionId,
                    TestStepId = PersistedStepId(step),
                    StepOrder = step.StepOrder,
                    StepSnapshot = CloneConfig(step.Config),
                    Status = ExecutionStatus.Skipped,
                    Log = "执行已被手动终止，未执行",
                };
                results.Add(cancelled);
                await TryNotifyCompletedAsync(onStepCompleted, cancelled);
                continue;
            }

            var stepSnapshot = CloneConfig(step.Config);
            await TryNotifyStartedAsync(onStepStarted, step.StepOrder, stepSnapshot);
            var result = new ExecutionResult
            {
                ExecutionId = executionId,
                TestStepId = PersistedStepId(step),
                StepOrder = step.StepOrder,
                StepSnapshot = stepSnapshot,
                StepActionType = (int)step.ActionType,
                Status = ExecutionStatus.Running,
            };
            var sw = Stopwatch.StartNew();
            try
            {
                switch (step.ActionType)
                {
                    case ActionType.Request:
                        lastResponse = await executor.ExecuteRequestAsync(step, testCase.Timeout, ct);
                        result.Log = ApiCaseExecutor.Summarize(lastResponse);
                        break;
                    case ActionType.AssertResponse:
                        if (lastResponse is null) throw new StepExecutionException("AssertResponse 前必须有 Request 步骤");
                        executor.AssertResponse(step, lastResponse);
                        result.Log = "响应断言通过";
                        break;
                    case ActionType.ExtractVariable:
                        if (lastResponse is null) throw new StepExecutionException("ExtractVariable 前必须有 Request 步骤");
                        executor.ExtractVariable(step, lastResponse);
                        result.Log = "变量提取成功";
                        break;
                    default:
                        throw new StepExecutionException($"{step.ActionType} 不适用于 API 用例");
                }
                result.Status = ExecutionStatus.Passed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 手动终止打断的步骤：单独标记，整条执行的终态由执行器定为 Canceled
                result.Status = ExecutionStatus.Canceled;
                result.ErrorMessage = "执行已被手动终止";
            }
            catch (Exception ex)
            {
                result.Status = ExecutionStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.StackTrace = ex.StackTrace;
                _logger.LogWarning(ex, "API 步骤 {Order} 执行失败", step.StepOrder);
                if (step.ActionType == ActionType.Request)
                    lastResponse = null;
            }
            sw.Stop();
            result.DurationMs = (int)sw.ElapsedMilliseconds;
            results.Add(result);
            await TryNotifyCompletedAsync(onStepCompleted, result);

            // 失败即中止：跳过剩余步骤并标记 Skipped
            if (testCase.FailFast && result.Status == ExecutionStatus.Failed)
            {
                foreach (var remaining in steps.Where(s => s.StepOrder > step.StepOrder))
                {
                    var skipped = new ExecutionResult
                    {
                        ExecutionId = executionId,
                        TestStepId = PersistedStepId(remaining),
                        StepOrder = remaining.StepOrder,
                        StepSnapshot = CloneConfig(remaining.Config),
                        StepActionType = (int)remaining.ActionType,
                        Status = ExecutionStatus.Skipped,
                        Log = "失败即中止（FailFast），未执行",
                    };
                    results.Add(skipped);
                    await TryNotifyCompletedAsync(onStepCompleted, skipped);
                }
                break;
            }
        }
        return results;
    }

    /// <summary>
    /// 执行单个步骤。返回值为 (可选的步骤日志, 内部重试消耗次数)——
    /// 只有部分动作会产生有意义的过程信息，其余日志返回 null；retriesUsed > 0 说明这次成功靠重试兜底。
    /// </summary>
    private async Task<(string? Log, int RetriesUsed)> ExecuteStepWithRetryAsync(
        IPage page, TestStep step, TestCase testCase, Environment? environment, CancellationToken ct)
    {
        // 已识别为不稳定的用例自动加一次重试（隔离）：抖动导致的偶发失败不再直接判失败，
        // 一旦稳定下来 flake 标记会被清除，重试也随之取消，不会无限放大执行时长。
        var attempts = Math.Max(1, testCase.RetryCount + 1 + (testCase.IsFlaky ? 1 : 0));
        Exception? last = null;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                return (await ExecuteStepAsync(page, step, testCase, environment, ct), i);
            }
            catch (Exception ex) when (i < attempts - 1)
            {
                last = ex;
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(last!).Throw();
        throw last!;
    }

    private async Task<string?> ExecuteStepAsync(
        IPage page, TestStep step, TestCase testCase, Environment? environment, CancellationToken ct)
    {
        var cfg = step.Config;
        var timeout = testCase.Timeout;
        switch (step.ActionType)
        {
            case ActionType.Navigate:
                if (string.IsNullOrWhiteSpace(cfg.Url))
                    throw new StepExecutionException("Navigate 缺少 URL");
                var resolvedUrl = ResolveAbsoluteGotoUrl(cfg.Url, ResolveBaseUrl(testCase, environment), "Navigate 步骤");
                var beforeUrl = page.Url;
                var targetPath = cfg.Url.TrimStart('/');
                _logger.LogInformation("Navigate: 前URL={Before}, 目标URL={Target}, raw={Raw}",
                    beforeUrl, resolvedUrl, cfg.Url);
                await page.GotoAsync(resolvedUrl,
                    new PageGotoOptions
                    {
                        Timeout = timeout,
                        WaitUntil = WaitUntilState.NetworkIdle,
                    });
                // GotoAsync(NetworkIdle) 在 SPA 里可能过早满足（vue-router 切换不一定发 HTTP），
                // 硬等 URL 真的包含目标路径才算数——如果路由守卫踢回 /login 这里会超时失败
                var targetLower = targetPath.ToLowerInvariant();
                var urlReached = await page.EvaluateAsync<bool>(
                    $@"async () => {{
                        const start = Date.now();
                        while (Date.now() - start < 15000) {{
                            if (location.pathname.toLowerCase().includes('{targetLower}')) return true;
                            await new Promise(r => setTimeout(r, 150));
                        }}
                        return false;
                    }}");
                var afterUrl = page.Url;
                _logger.LogWarning("Navigate: 完成后URL={After}, 与目标一致={Match}, 硬等到={UrlReached}",
                    afterUrl, afterUrl.Contains(targetPath, StringComparison.OrdinalIgnoreCase), urlReached);
                if (!urlReached)
                    throw new StepExecutionException(
                        $"Navigate 到 \"{cfg.Url}\" 后 URL 仍未达到目标（当前：\"{afterUrl}\"），" +
                        $"可能是路由守卫重定向、或页面还在过渡中。");
                break;

            case ActionType.Fill:
                await ExecuteWithLocatorAsync(page, step, testCase,
                    locator => page.Locator(locator).FillAsync(cfg.Value ?? string.Empty,
                        new LocatorFillOptions { Timeout = timeout }), ct);
                break;

            case ActionType.Click:
                await ExecuteWithLocatorAsync(page, step, testCase,
                    locator => page.Locator(locator).ClickAsync(new LocatorClickOptions { Timeout = timeout }), ct);
                break;

            case ActionType.Wait:
                if (cfg.Selector is { Value: not null } || cfg.Selector?.Type == "ai")
                {
                    await ExecuteWithLocatorAsync(page, step, testCase,
                        locator => page.Locator(locator).WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = timeout,
                        }), ct);
                }
                else
                {
                    await Task.Delay(PlaywrightStepMapper.ParseWaitMs(cfg.Value), ct);
                }
                break;

            case ActionType.Screenshot:
                break; // 每步结束后统一截图

            case ActionType.Scroll:
                if (cfg.Selector is { Value: not null })
                    await page.Locator(PlaywrightStepMapper.ToLocator(cfg.Selector)).ScrollIntoViewIfNeededAsync();
                else
                    await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
                break;

            case ActionType.AssertVisible:
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    // 在超时内等待可见（兼容延迟渲染），超时视为「未定位到」以触发 AI 自愈
                    try
                    {
                        await page.Locator(locator).First.WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = timeout,
                        });
                    }
                    catch (System.TimeoutException)
                    {
                        throw new LocatorNotResolvedException("元素不可见");
                    }
                }, ct);
                break;

            case ActionType.AssertText:
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    var loc = page.Locator(locator);
                    var expected = cfg.Value ?? string.Empty;
                    // input/textarea/select 的文本在 value 属性中（innerText 恒为空）
                    var tagName = await loc.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
                    var actual = tagName is "input" or "textarea" or "select"
                        ? await loc.InputValueAsync(new LocatorInputValueOptions { Timeout = timeout })
                        : await loc.InnerTextAsync(new LocatorInnerTextOptions { Timeout = timeout });
                    if (!actual.Contains(expected))
                        throw new StepExecutionException($"文本断言失败: 期望包含「{expected}」，实际「{actual}」");
                }, ct);
                break;

            case ActionType.AssertUrl:
                var expectedUrl = cfg.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(expectedUrl))
                    throw new StepExecutionException("AssertUrl 缺少期望 URL 片段（config.value）");
                if (!page.Url.Contains(expectedUrl, StringComparison.OrdinalIgnoreCase))
                    throw new StepExecutionException($"URL 断言失败: 期望包含「{expectedUrl}」，实际「{page.Url}」");
                break;

            case ActionType.AssertTitle:
                var expectedTitle = cfg.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(expectedTitle))
                    throw new StepExecutionException("AssertTitle 缺少期望标题片段（config.value）");
                var actualTitle = await page.TitleAsync();
                if (!actualTitle.Contains(expectedTitle, StringComparison.OrdinalIgnoreCase))
                    throw new StepExecutionException($"标题断言失败: 期望包含「{expectedTitle}」，实际「{actualTitle}」");
                break;

            case ActionType.AssertA11y:
                // config.value 是最低拦截级别（minor/moderate/serious/critical/none），默认 serious。
                // 按 minor 拦截会让绝大多数真实站点直接不通过——这种断言一旦遍地误报就没人看了。
                var (report, failure) = await _a11y.ScanAsync(page, cfg.Value, ct);
                if (failure is not null)
                    throw new StepExecutionException(failure);
                // 即使通过也把明细写进步骤日志：无障碍问题通常是「慢慢修」的，
                // 只有失败才可见的话，团队永远看不到自己离达标还有多远
                return A11yLog(report);

            // ---------------- 迭代 F：确定性的高频交互与断言
            // 这些此前只能靠 AIAction/AIAssert 兜底。AI 是不确定、慢、按调用计费的，
            // 用它顶替确定性动作是错的用法——下面的实现都走既有的定位+自愈链，逻辑是确定的。

            case ActionType.Select:
            {
                if (string.IsNullOrWhiteSpace(cfg.Value))
                    throw new StepExecutionException("Select 缺少要选中的选项（config.value）");
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    // Playwright 会按 value / label / 文本依次匹配，所以填哪个都能选上
                    await page.Locator(locator).SelectOptionAsync(cfg.Value,
                        new LocatorSelectOptionOptions { Timeout = timeout });
                }, ct);
                break;
            }

            case ActionType.UploadFile:
            {
                var files = ConfigValueParser.SplitFilePaths(cfg.Value);
                if (files.Length == 0)
                    throw new StepExecutionException("UploadFile 缺少文件路径（config.value）");
                // 提前给人话错误：SetInputFiles 对不存在的路径抛的是底层异常，
                // 用户根本看不出是自己路径写错了还是别的问题
                var missing = files.Where(f => !File.Exists(f)).ToArray();
                if (missing.Length > 0)
                    throw new StepExecutionException($"上传文件不存在: {string.Join(", ", missing)}");
                // SetInputFiles 会把文件整体读进内存；执行机上误填一个大文件
                // （比如把安装包路径当测试资源）会直接把执行进程吃爆，提前拒绝
                var tooBig = files.Where(f => new FileInfo(f).Length > MaxUploadFileBytes).ToArray();
                if (tooBig.Length > 0)
                    throw new StepExecutionException(
                        $"上传文件超过大小上限（{MaxUploadFileBytes / 1024 / 1024} MB）: {string.Join(", ", tooBig.Select(Path.GetFileName))}");
                await ExecuteWithLocatorAsync(page, step, testCase,
                    locator => page.Locator(locator).SetInputFilesAsync(files,
                        new LocatorSetInputFilesOptions { Timeout = timeout }), ct);
                break;
            }

            case ActionType.PressKey:
            {
                var key = cfg.Value?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    throw new StepExecutionException(
                        "PressKey 缺少按键名（config.value），例如 Enter、Escape、Control+A");
                if (cfg.Selector is { Value: not null } || cfg.Selector?.Type == "ai")
                    // 指定了定位符 → 先聚焦该元素再按键（表单里按 Enter 提交的常规写法）
                    await ExecuteWithLocatorAsync(page, step, testCase,
                        locator => page.Locator(locator).PressAsync(key,
                            new LocatorPressOptions { Timeout = timeout }), ct);
                else
                    // 没指定 → 发给当前焦点，用于关闭弹窗（Escape）这类全局按键
                    await page.Keyboard.PressAsync(key);
                break;
            }

            case ActionType.Hover:
                await ExecuteWithLocatorAsync(page, step, testCase,
                    locator => page.Locator(locator).HoverAsync(new LocatorHoverOptions { Timeout = timeout }), ct);
                break;

            case ActionType.AssertAttribute:
            {
                var attribute = cfg.Attribute?.Trim();
                if (string.IsNullOrWhiteSpace(attribute))
                    throw new StepExecutionException(
                        "AssertAttribute 缺少属性名（config.attribute），例如 disabled、aria-label、href");
                var expectedAttr = cfg.Value ?? string.Empty;
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    var actual = await page.Locator(locator).GetAttributeAsync(attribute,
                        new LocatorGetAttributeOptions { Timeout = timeout });
                    if (actual is null)
                        throw new StepExecutionException($"属性断言失败: 元素上没有「{attribute}」属性");
                    if (!actual.Contains(expectedAttr))
                        throw new StepExecutionException(
                            $"属性断言失败: 属性 {attribute} 期望包含「{expectedAttr}」，实际「{actual}」");
                }, ct);
                break;
            }

            case ActionType.AssertCount:
            {
                if (!int.TryParse(cfg.Value?.Trim(), out var expectedCount) || expectedCount < 0)
                    throw new StepExecutionException("AssertCount 需要非负整数作为期望个数（config.value）");
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    var actual = await page.Locator(locator).CountAsync();
                    if (actual != expectedCount)
                        throw new StepExecutionException($"个数断言失败: 期望 {expectedCount} 个，实际 {actual} 个");
                }, ct);
                break;
            }

            case ActionType.AssertValue:
            {
                var expectedValue = cfg.Value ?? string.Empty;
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    var actual = await page.Locator(locator).InputValueAsync(
                        new LocatorInputValueOptions { Timeout = timeout });
                    if (!actual.Contains(expectedValue))
                        throw new StepExecutionException(
                            $"输入值断言失败: 期望包含「{expectedValue}」，实际「{actual}」");
                }, ct);
                break;
            }

            case ActionType.AssertState:
            {
                var wantedState = cfg.Value?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(wantedState))
                    throw new StepExecutionException("AssertState 缺少状态名（config.value）");
                await ExecuteWithLocatorAsync(page, step, testCase, async locator =>
                {
                    var loc = page.Locator(locator);
                    // 一律用 Playwright 自己的判定，不自己读 DOM 属性比较：
                    // 可见性/可编辑性有层叠与继承语义，自己判会与 Playwright 的实际行为不一致
                    var (ok, actualDesc) = wantedState switch
                    {
                        "visible" => (await loc.IsVisibleAsync(), "不可见"),
                        "hidden" => (!await loc.IsVisibleAsync(), "可见"),
                        "enabled" => (await loc.IsEnabledAsync(), "已禁用"),
                        "disabled" => (!await loc.IsEnabledAsync(), "未禁用"),
                        "checked" => (await loc.IsCheckedAsync(), "未勾选"),
                        "unchecked" => (!await loc.IsCheckedAsync(), "已勾选"),
                        "editable" => (await loc.IsEditableAsync(), "不可编辑"),
                        "readonly" => (!await loc.IsEditableAsync(), "可编辑"),
                        _ => throw new StepExecutionException(
                            $"AssertState 不支持的状态「{wantedState}」，可选：" +
                            "visible/hidden/enabled/disabled/checked/unchecked/editable/readonly"),
                    };
                    if (!ok)
                        throw new StepExecutionException($"状态断言失败: 期望 {wantedState}，实际{actualDesc}");
                }, ct);
                break;
            }

            case ActionType.Request:
            case ActionType.AssertResponse:
            case ActionType.ExtractVariable:
                throw new StepExecutionException($"{step.ActionType} 属于接口测试，M5 支持");

            case ActionType.AIAction:
                throw new StepExecutionException($"{step.ActionType} 属于 AI 驱动，M5/M6 支持");

            case ActionType.AIAssert:
                var expectation = string.IsNullOrWhiteSpace(cfg.Value) ? step.AIInstruction : cfg.Value;
                if (string.IsNullOrWhiteSpace(expectation))
                    throw new StepExecutionException("AIAssert 缺少期望描述（config.value 或 aiInstruction）");
                var bodyText = await page.EvaluateAsync<string>(
                    "() => document.body ? document.body.innerText.slice(0, 3000) : ''");
                var inputValuesJson = await page.EvaluateAsync<string>("""
                    () => JSON.stringify(Array.from(document.querySelectorAll('input, textarea, select')).slice(0, 50).map(el => ({
                        tag: el.tagName.toLowerCase(),
                        id: el.id || null,
                        name: el.getAttribute('name') || null,
                        value: (el.value || '').toString().slice(0, 200)
                    })))
                    """);
                var inputValues = System.Text.Json.JsonSerializer.Deserialize<List<InputEvidence>>(
                    inputValuesJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var evidence = System.Text.Json.JsonSerializer.SerializeToElement(
                    new { url = page.Url, text = bodyText, inputs = inputValues });
                var verdict = await _aiClient.SmartAssertAsync(expectation, evidence, ct);
                if (!verdict.Passed)
                    throw new StepExecutionException($"AI 断言失败: {verdict.Reason}");
                break;

            default:
                throw new StepExecutionException($"未知动作: {step.ActionType}");
        }

        // 绝大多数动作没有需要留存的过程信息
        return null;
    }

    /// <summary>把无障碍扫描结果压成一行可读日志（通过时也记录，便于团队看到离达标还差多少）</summary>
    private static string A11yLog(A11yReport report)
    {
        if (report.Total == 0) return "无障碍扫描通过：未发现违规";
        return $"无障碍扫描通过：发现 {report.Total} 条违规但均低于拦截级别" +
               $"（致命 {report.Critical}、严重 {report.Serious}、中等 {report.Moderate}、轻微 {report.Minor}）：" +
               string.Join("；", report.Violations
                   .OrderByDescending(v => v.Impact)
                   .Take(5)
                   .Select(v => $"{v.Id}（{v.Impact}，{v.NodeCount} 处）"));
    }

    /// <summary>
    /// 生效的 BaseUrl：用例自身配置优先；未配置时自动使用执行环境的地址。
    /// </summary>
    private static string? ResolveBaseUrl(TestCase testCase, Environment? environment)
        => string.IsNullOrWhiteSpace(testCase.BaseUrl) ? environment?.BaseUrl : testCase.BaseUrl;

    /// <summary>
    /// 解析 Playwright 可接受的绝对 URL。
    /// 相对路径（如 /login、/datasets）会用 baseUrl 补齐；baseUrl 缺失时抛出带上下文的可读错误，
    /// 避免 Playwright 的 "Cannot navigate to invalid URL" 让人摸不着头脑。
    /// </summary>
    /// <param name="rawUrl">步骤或环境里填的原始 URL（可能是相对路径）</param>
    /// <param name="baseUrl">用来拼接的 BaseUrl（来自用例或环境）</param>
    /// <param name="context">调用上下文，如 "Navigate 步骤"、"自动登录地址"——决定错误提示怎么说</param>
    /// <returns>Playwright page.GotoAsync 可直接使用的绝对 URL</returns>
    private string ResolveAbsoluteGotoUrl(string? rawUrl, string? baseUrl, string context)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            throw new StepExecutionException($"{context} 缺少 URL");

        var resolved = UrlResolver.Resolve(rawUrl, baseUrl);
        var tryCreateOk = Uri.TryCreate(resolved, UriKind.Absolute, out _);
        _logger.LogWarning("解析 URL [{Context}]: raw={RawUrl}, base={BaseUrl}, resolved={Resolved}, IsAbsolute={TryCreateOk}",
            context, rawUrl, baseUrl ?? "(null)", resolved, tryCreateOk);

        if (tryCreateOk)
            return resolved;

        throw new StepExecutionException(
            $"{context} 使用了相对地址 \"{rawUrl}\"，但未配置 BaseUrl（当前值：\"{baseUrl ?? "(null)"}\"）无法拼出完整地址。" +
            $"解析结果是 \"{resolved}\"，不是 Playwright 可接受的绝对 URL。" +
            $"请在【执行环境】或【测试用例】里配置 BaseUrl（例如 http://localhost:5173），" +
            $"或把 {context} 直接改成完整的 http(s):// 地址。");
    }

    private async Task<ExecutionResult> TryAutoLoginAsync(
        IPage page, Environment environment, TestCase testCase, Guid executionId, CancellationToken ct)
    {
        // 用 ResolveAbsoluteGotoUrl 算绝对 URL，让 StepSnapshot 里存的是完整地址，
        // 前端步骤摘要显示出来就不会只是 "/login" —— 用户一眼能看出是在跑哪个环境
        var rawLoginUrl = environment.LoginUrl ?? environment.BaseUrl;
        var fullLoginUrl = ResolveAbsoluteGotoUrl(rawLoginUrl, environment.BaseUrl, "自动登录地址");
        var result = new ExecutionResult
        {
            ExecutionId = executionId,
            StepOrder = -1,
            StepSnapshot = new StepConfig { Url = fullLoginUrl },
            Status = ExecutionStatus.Running,
        };
        var sw = Stopwatch.StartNew();
        try
        {
            // 最终诊断日志：确保 GotoAsync 真收到了完整 URL
            _logger.LogWarning("TRY_AUTO_LOGIN_GOTO: fullLoginUrl=\"{Full}\", raw=\"{Raw}\", base=\"{Base}\"",
                fullLoginUrl, rawLoginUrl, environment.BaseUrl ?? "(null)");

            // fullLoginUrl 已在上面算出（StepSnapshot 和 Goto 共用同一个绝对 URL），不用再算一次
            await page.GotoAsync(fullLoginUrl, new PageGotoOptions
            {
                Timeout = testCase.Timeout,
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            // ⚠ 自动登录用确定性选择器，绕开 AI 定位链
            // AI 定位可能因向量命中相似度低(0.32)把密码填到用户名框里
            // 登录页结构稳定：input[type=text] 用户名 / input[type=password] 密码 / button.el-button--primary 提交
            var timeout = testCase.Timeout > 0 ? testCase.Timeout : 30000;

            var usernameInput = page.Locator("input[type=text], input[placeholder*=用户名], input[placeholder*=账号], .el-form-item input").First;
            await usernameInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
            await usernameInput.FillAsync(environment.LoginUsername);
            _logger.LogWarning("AUTO_LOGIN_STEP: 用户名已填充 selector=input[type=text]");

            var passwordInput = page.Locator("input[type=password]").First;
            await passwordInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
            await passwordInput.FillAsync(environment.LoginPassword);
            _logger.LogWarning("AUTO_LOGIN_STEP: 密码已填充 selector=input[type=password]");

            var loginBtn = page.Locator("button.el-button--primary, button[type=submit], .login-btn, button:has-text('登录')").First;
            await loginBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
            await loginBtn.ClickAsync();
            _logger.LogWarning("AUTO_LOGIN_STEP: 登录按钮已点击 selector=button.el-button--primary");

            // ⚠ 关键：点完登录按钮不能立刻判 Pass
            // SPA 登录流程：POST → token 写 storage → 路由守卫重定向 → 目标页加载
            // 先硬等 URL 真的变了（SPA 里 WebSocket/长轮询可能让 NetworkIdle 永远不满足，不能只靠它）
            var urlLeftLogin = await page.EvaluateAsync<bool>(
                @"async () => {
                    const start = Date.now();
                    while (Date.now() - start < 15000) {
                        if (!/login/i.test(location.pathname)) return true;
                        await new Promise(r => setTimeout(r, 150));
                    }
                    return false;
                }");
            var urlAfterClick = page.Url;
            _logger.LogInformation("自动登录: 点击后URL={}, 离开login={}", urlAfterClick, urlLeftLogin);

            // NetworkIdle 作为补充——确保路由切换 + 首屏数据请求都安静下来；失败不阻断
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = testCase.Timeout });
                _logger.LogInformation("自动登录: NetworkIdle 到达, 当前URL={}", page.Url);
            }
            catch (PlaywrightException ex)
            {
                _logger.LogWarning(ex, "自动登录: NetworkIdle 超时或失败, 继续执行, 当前URL={}", page.Url);
            }

            // 再补一道：URL 确实已经离开了登录页（防止路由守卫又重定向回来但 NetworkIdle 已经到了）
            var currentUrl = page.Url;
            if (currentUrl.Contains("login", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(environment.LoginSuccessIndicator))
            {
                throw new StepExecutionException(
                    $"点击登录后 URL 仍在 \"{currentUrl}\"（疑似登录失败或未配置登录成功判定条件）。" +
                    $"建议在【执行环境】里填写 LoginSuccessIndicator，或排查登录账号/密码是否正确。");
            }

            if (!string.IsNullOrWhiteSpace(environment.LoginSuccessIndicator))
            {
                var bodyText = await page.EvaluateAsync<string>(
                    "() => document.body ? document.body.innerText.slice(0, 3000) : ''");
                var evidence = System.Text.Json.JsonSerializer.SerializeToElement(
                    new { url = page.Url, text = bodyText });
                var verdict = await _aiClient.SmartAssertAsync(environment.LoginSuccessIndicator, evidence, ct);
                if (!verdict.Passed)
                    throw new StepExecutionException($"登录验证失败: {verdict.Reason}");
            }

            result.Status = ExecutionStatus.Passed;
            result.Log = "自动登录成功";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 手动终止打断登录：单独标记，整条执行的终态由执行器定为 Canceled
            result.Status = ExecutionStatus.Canceled;
            result.ErrorMessage = "执行已被手动终止";
        }
        catch (Exception ex)
        {
            result.Status = ExecutionStatus.Error;
            result.ErrorMessage = $"自动登录失败: {ex.Message}";
            result.StackTrace = ex.StackTrace;
            _logger.LogWarning(ex, "自动登录失败");
        }
        sw.Stop();
        result.DurationMs = (int)sw.ElapsedMilliseconds;
        return result;
    }

    // 临时步骤走既有 AI 定位自愈链：selector type=ai + description
    private Task FillByDescriptionAsync(
        IPage page, TestCase testCase, string description, string value, CancellationToken ct)
    {
        var step = new TestStep
        {
            Config = new StepConfig
            {
                Selector = new SelectorConfig { Type = "ai", Description = description },
                Value = value,
            },
        };
        return ExecuteWithLocatorAsync(page, step, testCase,
            locator => page.Locator(locator).FillAsync(value,
                new LocatorFillOptions { Timeout = testCase.Timeout }), ct);
    }

    private Task ClickByDescriptionAsync(
        IPage page, TestCase testCase, string description, CancellationToken ct)
    {
        var step = new TestStep
        {
            Config = new StepConfig
            {
                Selector = new SelectorConfig { Type = "ai", Description = description },
            },
        };
        return ExecuteWithLocatorAsync(page, step, testCase,
            locator => page.Locator(locator).ClickAsync(
                new LocatorClickOptions { Timeout = testCase.Timeout }), ct);
    }

    private async Task TryNotifyCompletedAsync(Func<ExecutionResult, Task>? onStepCompleted, ExecutionResult result)
    {
        if (onStepCompleted is null)
            return;
        try
        {
            await onStepCompleted(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "步骤完成回调失败 步骤 {Order}", result.StepOrder);
        }
    }

    private async Task TryNotifyStartedAsync(
        Func<int, StepConfig?, Task>? onStepStarted, int stepOrder, StepConfig? snapshot)
    {
        if (onStepStarted is null)
            return;
        try
        {
            await onStepStarted(stepOrder, snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "步骤开始回调失败 步骤 {Order}", stepOrder);
        }
    }

    /// <summary>
    /// 注册用例级网络规则（页面打开后、任何请求发出前调用）。
    ///
    /// 这是"按用例把第三方依赖桩掉"的落点：支付、短信、地图、埋点这类接口抖一下，
    /// 用例就红一次，而它跟被测系统的质量毫无关系。
    /// </summary>
    private async Task ApplyNetworkRulesAsync(IPage page, TestCase testCase)
    {
        IReadOnlyList<NetworkRule>? rules;
        try
        {
            rules = NetworkRuleSet.Parse(testCase.NetworkRules);
        }
        catch (InvalidOperationException ex)
        {
            // 库里存着非法规则（手工改库、或被跳过校验的旧数据）不该让整条执行崩掉，
            // 但必须在日志里留痕——否则"规则静默没生效"会被当成业务 bug 查很久
            _logger.LogWarning("用例 {TestCaseId} 的网络规则无法解析，本次不生效：{Message}",
                testCase.Id, ex.Message);
            return;
        }

        if (rules is null || rules.Count == 0)
            return;

        foreach (var rule in rules)
        {
            var captured = rule;
            await page.RouteAsync(captured.Pattern, async route =>
            {
                try
                {
                    switch (captured.Action)
                    {
                        case NetworkRuleAction.Abort:
                            await route.AbortAsync();
                            break;

                        case NetworkRuleAction.Delay:
                            if (captured.DelayMs is > 0)
                                await Task.Delay(captured.DelayMs.Value);
                            await route.ContinueAsync();
                            break;

                        default:
                            await route.FulfillAsync(new RouteFulfillOptions
                            {
                                Status = captured.Status ?? 200,
                                ContentType = captured.ContentType ?? "application/json",
                                Body = captured.Body ?? string.Empty,
                            });
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // 单条规则处理失败不能把整条执行带崩：兜底放行，让请求按原样发出
                    _logger.LogWarning(ex, "网络规则处理失败（已放行）：{Pattern}", captured.Pattern);
                    try
                    {
                        await route.ContinueAsync();
                    }
                    catch
                    {
                        // 请求已经被处理过了（例如已 abort），再 continue 会抛——忽略即可
                    }
                }
            });
        }

        _logger.LogInformation("用例 {TestCaseId} 已注册 {Count} 条网络规则",
            testCase.Id, rules.Count);
    }

    private async Task ExecuteWithLocatorAsync(
        IPage page, TestStep step, TestCase testCase,
        Func<string, Task> action, CancellationToken ct)
    {
        var cfg = step.Config;
        var description = string.IsNullOrWhiteSpace(cfg.Selector?.Description)
            ? step.AIElementDescription
            : cfg.Selector!.Description;
        var isAi = string.Equals(cfg.Selector?.Type, "ai", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(cfg.Selector?.Value);

        if (isAi)
        {
            var located = await LocateWithAIAsync(page, step, testCase, description, ct);
            try
            {
                await action(located.Locator);
            }
            catch (Exception ex) when (ex is System.TimeoutException or LocatorNotResolvedException)
            {
                // 缓存/旧定位失效：跳过缓存重新 LLM 定位并重试，成功后覆盖写缓存
                located = await LocateWithAIAsync(page, step, testCase, description, ct, skipCache: true);
                await action(located.Locator);
                if (!string.IsNullOrWhiteSpace(description))
                    await _cache.SaveAsync(testCase.ProjectId, page.Url, description,
                        located.SelectorType, located.SelectorValue, located.Confidence, ct);
                return;
            }
            if (!located.FromCache && !string.IsNullOrWhiteSpace(description))
                await _cache.SaveAsync(testCase.ProjectId, page.Url, description,
                    located.SelectorType, located.SelectorValue, located.Confidence, ct);
            return;
        }

        var locator = PlaywrightStepMapper.ToLocator(cfg.Selector);
        var canHeal = !string.IsNullOrWhiteSpace(description);

        // 快速探测：带元素描述的步骤先用短超时确认选择器是否存在，
        // 未命中直接进入自愈链，省去「整步超时（默认 30s）后才自愈」的等待
        if (canHeal && _options.SelectorProbeEnabled)
        {
            var probeMs = Math.Clamp(_options.SelectorProbeTimeoutMs, 200, testCase.Timeout);
            if (!await ProbeSelectorAsync(page, locator, probeMs))
            {
                _logger.LogInformation(
                    "选择器快速探测未命中（{ProbeMs}ms），转 AI 自愈定位：{Selector}", probeMs, locator);
                try
                {
                    await HealWithAiAsync(page, step, testCase, description!, action, ct, locator);
                    return;
                }
                catch (StepExecutionException ex) when (ex is not LocatorNotResolvedException)
                {
                    // 自愈无法定位（例如页面确实尚未渲染、AI 也匹配不到）：
                    // 回退到原始选择器并使用完整超时，保持既有语义不退化
                    _logger.LogWarning("自愈未成功（{Message}），回退原始选择器并等待完整超时：{Selector}",
                        ex.Message, locator);
                }
            }
        }

        try
        {
            await action(locator);
        }
        catch (Exception ex) when ((ex is System.TimeoutException or LocatorNotResolvedException)
                                   && !string.IsNullOrWhiteSpace(description))
        {
            await HealWithAiAsync(page, step, testCase, description!, action, ct, locator);
        }
    }

    /// <summary>
    /// 快速探测选择器是否存在于 DOM（命中即返回，不等可见）。
    /// 探测超时返回 false，由调用方转 AI 自愈。
    /// </summary>
    private async Task<bool> ProbeSelectorAsync(IPage page, string locator, int probeMs)
    {
        if (probeMs <= 0)
            return true;
        try
        {
            await page.Locator(locator).First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = probeMs,
            });
            return true;
        }
        catch (System.TimeoutException)
        {
            return false;
        }
        catch (PlaywrightException ex)
        {
            // 选择器语法无效等情况：同样交给自愈链兜底（自愈失败会回退原始选择器并抛出真实错误）
            _logger.LogInformation(ex, "选择器探测异常，转自愈：{Selector}", locator);
            return false;
        }
    }

    /// <summary>
    /// AI 自愈链：缓存选择器 → LLM 定位 → 重试，成功后回写缓存。
    /// </summary>
    private async Task HealWithAiAsync(
        IPage page, TestStep step, TestCase testCase, string description,
        Func<string, Task> action, CancellationToken ct, string? failedLocator = null)
    {
        var cached = await _cache.GetSelectorAsync(testCase.ProjectId, page.Url, description, ct);
        if (cached is not null)
        {
            try
            {
                await action(PlaywrightStepMapper.ToLocator(new SelectorConfig
                {
                    Type = cached.Type,
                    Value = cached.Value,
                }));
                return;
            }
            catch (Exception ex) when (ex is System.TimeoutException or LocatorNotResolvedException)
            {
                _logger.LogInformation("缓存选择器已失效，改用 LLM 重新定位：{Description}", description);
            }
        }

        // AI Worker 不可用（未部署/宕机/超时）时把底层异常转成业务异常：
        // - 「选择器快速探测未命中」路径的回退 catch 只认 StepExecutionException，
        //   底层 AIWorkerException 逸出会让步骤直接 Error，而不是按设计回退原始选择器；
        // - 超时路径上它向上传播时也携带更可读的错误信息（AI 挂了 + 哪个描述没定位到）。
        LocatedSelector located;
        try
        {
            located = await LocateWithAIAsync(page, step, testCase, description, ct, skipCache: true);
        }
        catch (AIWorkerException ex)
        {
            _logger.LogWarning("AI 自愈不可用，元素描述 {Description}：{Message}", description, ex.Message);
            throw new StepExecutionException($"AI 自愈不可用（{ex.Message}），元素描述：{description}");
        }

        try
        {
            await action(located.Locator);
        }
        catch (Exception ex) when (_options.HealedActionMinTimeoutMs > 0
                                   && (ex is System.TimeoutException or LocatorNotResolvedException))
        {
            // LLM 刚定位到的元素可能仍在渲染中：短暂等待后重试一次
            _logger.LogInformation(ex, "自愈定位后执行失败，等待 {Delay}ms 重试：{Description}",
                _options.HealedActionMinTimeoutMs, description);
            await Task.Delay(_options.HealedActionMinTimeoutMs, ct);
            await action(located.Locator);
        }
        await _cache.SaveAsync(testCase.ProjectId, page.Url, description,
            located.SelectorType, located.SelectorValue, located.Confidence, ct);
        if (!string.IsNullOrWhiteSpace(failedLocator))
            _logger.LogInformation("已用 AI 选择器 {New} 替换失效选择器 {Old}",
                located.SelectorValue, failedLocator);
    }

    private async Task<LocatedSelector> LocateWithAIAsync(
        IPage page, TestStep step, TestCase testCase, string? description, CancellationToken ct,
        bool skipCache = false)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new StepExecutionException("AI 定位需要元素描述（selector.description 或 aiElementDescription）");
        if (!skipCache)
        {
            var cached = await _cache.GetSelectorAsync(testCase.ProjectId, page.Url, description, ct);
            if (cached is not null)
                return new LocatedSelector(
                    PlaywrightStepMapper.ToLocator(new SelectorConfig { Type = cached.Type, Value = cached.Value }),
                    cached.Type, cached.Value, 1f, FromCache: true);
        }

        var elements = await DomExtractor.ExtractAsync(page);
        if (elements.Count == 0)
            throw new StepExecutionException("页面无可交互元素，无法 AI 定位");
        var result = await _aiClient.LocateElementAsync(
            new LocateElementRequestDto(DomExtractor.NormalizePageUrl(page.Url), description, elements), ct);
        if (result.MatchedIndex < 0 || result.MatchedIndex >= elements.Count || result.Confidence < 0.5f)
            throw new StepExecutionException($"AI 定位失败: {result.Reasoning}");
        var selector = SelectorBuilder.Build(elements[result.MatchedIndex])
            ?? throw new StepExecutionException($"无法为元素 {result.MatchedIndex} 生成稳定选择器");
        return new LocatedSelector(
            PlaywrightStepMapper.ToLocator(selector), selector.Type, selector.Value!,
            result.Confidence, FromCache: false);
    }

    private sealed record LocatedSelector(
        string Locator, string SelectorType, string SelectorValue, float Confidence, bool FromCache);

    private sealed record InputEvidence(string Tag, string? Id, string? Name, string Value);

    private static StepConfig CloneConfig(StepConfig source) => new()
    {
        Url = source.Url,
        Selector = source.Selector is null ? null : new SelectorConfig
        {
            Type = source.Selector.Type,
            Description = source.Selector.Description,
            Value = source.Selector.Value,
        },
        Method = source.Method,
        Endpoint = source.Endpoint,
        Headers = source.Headers?.Select(h => new HeaderEntry { Name = h.Name, Value = h.Value }).ToList(),
        Body = source.Body,
        Value = source.Value,
    };
}
