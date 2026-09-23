using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// M8 Agent 失败自愈闭环编排（见 docs/m8-agent-design.md §3 / §4 / §9）。
///
/// 流程：门控 → 归因(AIClient) → 应用修复到**步骤副本** → 全用例重跑 → 判定 → 落 AgentAttempt。
/// 全程"尽力而为"：任何 LLM/执行异常都不上抛（不拖垮已完成的执行），最坏情况等价于未启用。
///
/// Phase 1 取舍：重跑走「全用例重跑 + 步骤覆写」（复用 <see cref="TestRunner.RunAsync"/>），
/// 不做"复用同一 IPage 的单步重跑"——后者需改 TestRunner 内部结构，风险高，留待 Phase 2。
/// </summary>
public class AgentLoopService
{
    private readonly TestDbContext _db;
    private readonly TestRunner _runner;
    private readonly AIClient _ai;
    private readonly AILivenessBreaker _breaker;
    private readonly SettingsService _settings;
    private readonly Notifications.InAppNotificationService _notifications;
    private readonly AgentLoopOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AgentLoopService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AgentLoopService(TestDbContext db, TestRunner runner, AIClient ai, AILivenessBreaker breaker,
        SettingsService settings, Notifications.InAppNotificationService notifications,
        IOptions<AgentLoopOptions> options, IMemoryCache cache, ILogger<AgentLoopService> logger)
    {
        _db = db;
        _runner = runner;
        _ai = ai;
        _breaker = breaker;
        _settings = settings;
        _notifications = notifications;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>入口。失败执行落库后由 ExecutionWorker 调用；自身吞掉异常。</summary>
    public async Task RunAsync(Guid executionId, CancellationToken ct)
    {
        try
        {
            await RunCoreAsync(executionId, ct);
        }
        catch (OperationCanceledException)
        {
            // 停机/取消：静默退出，不写任何结论
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent 自愈闭环异常 execution={ExecutionId}", executionId);
        }
    }

    private async Task RunCoreAsync(Guid executionId, CancellationToken ct)
    {
        var execution = await _db.Executions
            .Include(e => e.TestCase).ThenInclude(t => t!.Steps)
            .Include(e => e.Results)
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
        if (execution?.TestCase is null)
            return;
        if (execution.Status is not (ExecutionStatus.Failed or ExecutionStatus.Error))
            return;

        // ---- 门控：系统级 + 项目级双层与门 + 场景跳过 ----
        var systemConfig = await _settings.GetAsync(ct);
        if (!systemConfig.AgentLoopEnabled)
            return;

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == execution.TestCase.ProjectId, ct);
        if (project is null || !project.AgentLoopEnabled)
            return;

        // Agent 自愈熔断（AgentHealCircuitBreaker）：冷却期内不跑；冷却 24h 到期自动解除
        if (project.AgentLoopSuspendedAt is { } suspendedAt)
        {
            if (DateTime.UtcNow - suspendedAt < TimeSpan.FromHours(24))
                return;
            project.AgentLoopSuspendedAt = null;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("项目 {ProjectId} 的 Agent 自愈熔断冷却到期，已自动解除", project.Id);
        }

        if (_options.SkipAgentLoopInSuite && execution.SuiteRunId is not null)
            return;
        if (_options.SkipAgentLoopInCi && !string.IsNullOrWhiteSpace(execution.CommitSha))
            return;

        // AI 不可用（熔断打开）：直接降级，不发起任何 LLM 调用
        if (_breaker.IsOpen)
            return;

        var failed = execution.Results
            .Where(r => r.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
            .OrderBy(r => r.StepOrder)
            .Take(20)
            .ToList();
        if (failed.Count == 0)
            return;

        var testCase = execution.TestCase;
        var evidence = failed.Select(r => new FailedStepEvidence(
            r.StepOrder,
            r.StepSnapshot is null ? "未知" : JsonSerializer.Serialize(r.StepSnapshot, JsonOpts),
            r.ErrorMessage is null ? null : Truncate(r.ErrorMessage, 300),
            r.Log,
            r.StepSnapshot,
            // 元素快照可能较大（上限 250 个元素）：截断后再送 LLM，避免请求体过大
            Truncate(r.ElementSnapshot, 12000))).ToList();
        var similar = await FindSimilarAsync(failed, executionId, ct);

        var testCaseSummary = new Dictionary<string, object?>
        {
            ["name"] = testCase.Name,
            ["type"] = testCase.Type.ToString(),
            ["browser"] = execution.BrowserVersion,
            ["baseUrl"] = testCase.BaseUrl,
        };

        var maxCalls = execution.PlanRoundId is not null
            ? Math.Min(_options.MaxLlmCallsPerExecution, _options.MaxLlmCallsPerPlanRound)
            : _options.MaxLlmCallsPerExecution;
        var maxAttempts = Math.Max(1, _options.MaxFixAttemptsPerFailure);
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(1000, _options.MaxAgentLoopDurationMs));

        List<TestStep>? resolvedBase = null;
        var calls = 0;
        var attempts = 0;
        var healed = false;
        var budgetHit = false;
        // 自愈成功那一次的重跑结果：用于把持久化的步骤行对齐为"通过"（否则详情页会自相矛盾）
        List<ExecutionResult>? healedRerun = null;
        // 需人工审批的尝试：循环结束后统一通知审批人（此时记录尚未入库，先攒起来）
        var pendingApprovals = new List<AgentAttempt>();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (calls >= maxCalls || DateTime.UtcNow >= deadline)
            {
                budgetHit = true;
                break;
            }
            if (ct.IsCancellationRequested)
                break;

            // 熔断已下沉到 AIClient（所有 AI 调用的统一入口），这里只做优雅降级，不再重复包裹。
            // D3：以「用例指纹 + 错误签名 + 尝试序」为键查/写归因结果缓存，跳过重复 LLM 调用。
            var attributed = await GetAttributionAsync(testCase, failed, testCaseSummary, evidence, similar, attempt, ct);
            calls++;
            if (attributed is null)
                break; // LLM 不可用 / 熔断：预算已计一次，直接结束

            attempts = attempt;
            var record = new AgentAttempt
            {
                Id = Guid.NewGuid(),
                ExecutionId = executionId,
                AttemptNumber = attempt,
                TargetStepOrder = failed[0].StepOrder,
                FailureEvidence = JsonSerializer.Serialize(evidence, JsonOpts),
                DiagnosisRaw = Truncate(JsonSerializer.Serialize(attributed, JsonOpts), 8000),
                FixCategory = attributed.FixCategory,
                Confidence = attributed.Confidence,
                FixSummary = Truncate(attributed.SuggestedFix, 2000),
                NeedsApproval = attributed.NeedsHumanApproval,
                // 提议的动作必须在这里就留痕：下面的审批分支会直接 break，
                // 走不到「应用修复」那段（AppliedActions 对它永远为空），
                // 而人工「采纳」需要原始动作才能落库。不截断——截断会让 JSON 失效。
                ProposedFixes = attributed.ProposedFixes.Count > 0
                    ? JsonSerializer.Serialize(attributed.ProposedFixes, JsonOpts)
                    : null,
                CreatedAt = DateTime.UtcNow,
            };
            _db.AgentAttempts.Add(record);

            // 不可修复类：直接标记跳过，不应用
            if (attributed.FixCategory is FixCategory.AppBug
                or FixCategory.EnvironmentIssue or FixCategory.DataIssue or FixCategory.Unknown)
            {
                record.Result = AgentAttemptResult.Skipped;
                record.CompletedAt = DateTime.UtcNow;
                break;
            }

            // 需人工审批：LLM 明确要求，或含**破坏性动作**（增/删/重排/放宽断言）。
            // 后者**不信任** LLM 的 needs_approval 标记，由服务端强制走审批（纵深防御：
            // 页面文本里的提示注入可能诱导 LLM 把删除标成"低风险"）。
            var requiresApproval = attributed.NeedsHumanApproval
                || attributed.ProposedFixes.Any(f => FixActionApplier.IsDestructive(f.ActionType));
            if (requiresApproval)
            {
                record.NeedsApproval = true;
                record.Result = AgentAttemptResult.Skipped;
                record.CompletedAt = DateTime.UtcNow;
                pendingApprovals.Add(record);
                break;
            }
            // 未达置信度门：不自动应用，也不打扰人工
            if (!MeetsGate(attributed.FixCategory, attributed.Confidence))
            {
                record.Result = AgentAttemptResult.Skipped;
                record.CompletedAt = DateTime.UtcNow;
                break;
            }

            // 构造步骤副本（不挂 DbContext），应用修复
            resolvedBase ??= await _runner.ResolveStepsAsync(testCase, execution.Variables, ct);
            var copy = DeepCloneSteps(resolvedBase);
            var applied = new List<FixActionDto>();
            foreach (var fix in attributed.ProposedFixes)
            {
                if (FixActionApplier.TryApply(copy, fix, out var err, testCase.BaseUrl))
                    applied.Add(fix);
                else if (err is not null)
                    _logger.LogInformation("Agent 修复动作未应用（{Error}）", err);
            }
            record.AppliedActions = applied.Count > 0 ? JsonSerializer.Serialize(applied, JsonOpts) : null;
            record.AppliedSuccessfully = applied.Count > 0;

            if (applied.Count == 0)
            {
                record.Result = AgentAttemptResult.Skipped;
                record.CompletedAt = DateTime.UtcNow;
                break;
            }

            // 全用例重跑（注入副本；结果不落库，仅用于判定）
            List<ExecutionResult> rerun;
            try
            {
                var environment = await LoadEnvironmentAsync(execution.EnvironmentId, ct);
                rerun = await _runner.RunAsync(execution, environment, ct, stepOverride: copy);
            }
            catch (Exception ex)
            {
                record.Result = AgentAttemptResult.Failed;
                record.FailureAfterFix = Truncate(ex.Message, 2000);
                record.CompletedAt = DateTime.UtcNow;
                break;
            }

            var allPassed = rerun.Count > 0 && rerun.All(r => r.Status == ExecutionStatus.Passed);
            if (allPassed)
            {
                record.Result = AgentAttemptResult.Fixed;
                record.CompletedAt = DateTime.UtcNow;
                healed = true;
                healedRerun = rerun;
                break;
            }

            var firstFail = rerun.FirstOrDefault(r => r.Status != ExecutionStatus.Passed);
            record.Result = AgentAttemptResult.Failed;
            record.FailureAfterFix = Truncate(firstFail?.ErrorMessage, 2000);
            record.CompletedAt = DateTime.UtcNow;
            // 继续下一轮（是否换策略由下一轮归因决定）
        }

        // ---- 执行级结论落库 ----
        execution.AgentLoopCount = attempts;
        execution.AgentBudgetUsed = calls;
        if (healed)
        {
            execution.Status = ExecutionStatus.Passed;
            execution.AgentHealed = true;
            execution.AgentFinalVerdict = AgentAttemptResult.Fixed;
        }
        else
        {
            execution.AgentFinalVerdict = budgetHit ? AgentAttemptResult.BudgetExhausted : AgentAttemptResult.Failed;
        }

        // 自愈成功：把持久化的步骤行对齐为重跑结果，避免「状态通过、步骤仍失败」的自相矛盾
        if (healed && healedRerun is { Count: > 0 })
            await ReconcileHealedResultsAsync(executionId, healedRerun, ct);

        await _db.SaveChangesAsync(ct);

        // 待审批通知：审批人在消息中心可见（站内消息不受 NotifyEnabled 约束）
        foreach (var pending in pendingApprovals)
        {
            await _notifications.PushToPermissionAsync(Permission.ManageTestCases, new NotificationDraft(
                NotificationCategory.Execution,
                "Agent 修复待人工审批",
                NotificationLevel.Warning,
                $"执行失败后 Agent 建议「{pending.FixCategory}」类修复，需人工确认：{pending.FixSummary}",
                LinkUrl: $"/executions/{executionId}",
                LinkLabel: "查看执行",
                SourceType: "AgentAttempt",
                SourceId: pending.Id,
                ProjectId: execution.TestCase?.ProjectId));
        }

        if (healed)
            _logger.LogInformation("执行 {ExecutionId} 经 Agent 自愈通过（{Attempts} 次尝试）", executionId, attempts);
        else if (attempts > 0)
        {
            _logger.LogInformation("执行 {ExecutionId} Agent 自愈未通过（{Attempts} 次尝试，{Calls} 次 LLM）",
                executionId, attempts, calls);
            await TryTripCircuitBreakerAsync(project, executionId, ct);
        }
    }

    /// <summary>
    /// 自愈成功后，把持久化的步骤结果与**重跑结果**对齐（按 StepOrder 一一对应）。
    ///
    /// 为什么必须做：执行状态此时已被改写为 Passed + AgentHealed，但库里每个步骤行仍是原始那次
    /// 的终态（失败），详情页就会出现「状态：通过 / 步骤：失败」的自相矛盾。
    /// 只覆盖重跑里确实存在的 StepOrder；并在日志里注明该步骤是经 Agent 自愈通过的，保留可追溯性。
    /// </summary>
    private async Task ReconcileHealedResultsAsync(
        Guid executionId, IReadOnlyList<ExecutionResult> rerun, CancellationToken ct)
    {
        var byOrder = rerun
            .GroupBy(r => r.StepOrder)
            .ToDictionary(g => g.Key, g => g.Last());

        var persisted = await _db.ExecutionResults
            .Where(r => r.ExecutionId == executionId)
            .ToListAsync(ct);

        foreach (var row in persisted)
        {
            if (!byOrder.TryGetValue(row.StepOrder, out var fresh))
                continue;
            // 只有**原本失败**的步骤才是"被 Agent 修复的"；原本就通过的步骤不该被标注，
            // 否则日志会误导成"它也是 Agent 修的"。
            var wasFailing = row.Status is ExecutionStatus.Failed or ExecutionStatus.Error;
            row.Status = fresh.Status;
            row.ErrorMessage = fresh.ErrorMessage;
            row.StackTrace = fresh.StackTrace;
            row.DurationMs = fresh.DurationMs;
            if (!string.IsNullOrEmpty(fresh.ScreenshotUrl))
                row.ScreenshotUrl = fresh.ScreenshotUrl;
            if (wasFailing)
            {
                const string marker = "[Agent 自愈] 已由 Agent 修复并重跑通过";
                row.Log = string.IsNullOrEmpty(row.Log) ? marker : $"{row.Log}\n{marker}";
            }
        }

        _logger.LogInformation("执行 {ExecutionId} 已对齐 {Count} 条步骤结果为自愈后终态", executionId, persisted.Count);
    }

    /// <summary>
    /// Agent 自愈熔断真判定（AgentHealCircuitBreaker，见设计 §11）：
    /// 以 **DB 滚动窗口**判定（跨实例一致）——近 24h 该项目自愈"失败/预算耗尽"的执行数达阈值、且窗口内**无任何成功**，
    /// 则暂停该项目自愈 24h 并告警管理员。窗口内只要有一次成功就绝不熔断（说明链路是通的）。
    /// </summary>
    private async Task TryTripCircuitBreakerAsync(Project project, Guid executionId, CancellationToken ct)
    {
        try
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var caseIds = _db.TestCases.Where(t => t.ProjectId == project.Id).Select(t => t.Id);

            var failedExecutions = await _db.AgentAttempts.AsNoTracking()
                .Where(a => a.CreatedAt >= since
                            && (a.Result == AgentAttemptResult.Failed || a.Result == AgentAttemptResult.BudgetExhausted)
                            && a.Execution!.TestCaseId != null && caseIds.Contains(a.Execution.TestCaseId.Value))
                .Select(a => a.ExecutionId)
                .Distinct()
                .CountAsync(ct);
            if (failedExecutions < _options.ConsecutiveFailuresBeforeCircuitBreak)
                return;

            var fixedExecutions = await _db.AgentAttempts.AsNoTracking()
                .Where(a => a.CreatedAt >= since && a.Result == AgentAttemptResult.Fixed
                            && a.Execution!.TestCaseId != null && caseIds.Contains(a.Execution.TestCaseId.Value))
                .Select(a => a.ExecutionId)
                .Distinct()
                .CountAsync(ct);
            if (fixedExecutions > 0)
                return;

            project.AgentLoopSuspendedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("项目 {ProjectId} Agent 自愈已熔断：近 24h {Count} 次失败且无成功，冷却 24h",
                project.Id, failedExecutions);

            await _notifications.PushToPermissionAsync(Permission.ManageTestCases, new NotificationDraft(
                NotificationCategory.System,
                "Agent 自愈已熔断",
                NotificationLevel.Warning,
                $"项目「{project.Name}」近 24 小时有 {failedExecutions} 次 Agent 自愈失败且无成功，已暂停该项目 Agent 自愈 24 小时，请排查后重试。",
                LinkUrl: $"/projects",
                LinkLabel: "项目管理",
                SourceType: "AgentCircuitBreaker",
                SourceId: project.Id,
                ProjectId: project.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 熔断判定失败不能影响已落库的执行结论
            _logger.LogWarning(ex, "Agent 自愈熔断判定异常 project={ProjectId}", project.Id);
        }
    }

    /// <summary>置信度门（只读 FixCategory）。破坏性类永远不自动（走审批）。</summary>
    private bool MeetsGate(FixCategory category, float confidence) => category switch
    {
        FixCategory.LocatorUpdate => confidence >= _options.AutoApplyMinConfidence,
        FixCategory.WaitStrategy => confidence >= 0.6f,
        FixCategory.StepConfigPatch => confidence >= 0.7f,
        _ => false,
    };

    private async Task<List<SimilarCaseEvidence>> FindSimilarAsync(
        List<ExecutionResult> failed, Guid executionId, CancellationToken ct)
    {
        foreach (var f in failed)
        {
            if (string.IsNullOrWhiteSpace(f.ErrorMessage))
                continue;
            var prefix = f.ErrorMessage[..Math.Min(80, f.ErrorMessage.Length)];
            var similar = await _db.ExecutionResults.AsNoTracking()
                .Where(r => r.Status != ExecutionStatus.Passed && r.ExecutionId != executionId
                            && r.ErrorMessage != null && r.ErrorMessage.StartsWith(prefix))
                .OrderByDescending(r => r.CreatedAt)
                .Take(3)
                .Select(r => new SimilarCaseEvidence(r.StepOrder, "历史", r.ErrorMessage!))
                .ToListAsync(ct);
            if (similar.Count > 0)
                return similar;
        }
        return new List<SimilarCaseEvidence>();
    }

    /// <summary>
    /// D3 归因结果缓存：以「用例指纹 + 错误签名 + 尝试序」为键。命中则跳过 LLM 调用直接复用，
    /// 未命中则调用 <see cref="AIClient.AttributeAsync"/> 并写回缓存（TTL 取 <see cref="AgentLoopOptions.AttributionCacheTtlMinutes"/>）。
    ///
    /// 安全边界：缓存只存储 LLM 的「诊断结论」，下游的置信度门 / 破坏性动作强制审批 / 重跑验证仍逐次执行，
    /// 因此复用缓存绝不会绕过任何安全闸门或降低验证强度——最坏情况只是一次本就失败的修复被重试。
    /// 键含 attempt：保证同一执行内多轮尝试互不命中（保留策略变化），只在「不同执行、相同失败」时去重。
    /// </summary>
    private async Task<AttributedResultDto?> GetAttributionAsync(
        TestCase testCase, List<ExecutionResult> failed,
        Dictionary<string, object?> testCaseSummary, List<FailedStepEvidence> evidence,
        List<SimilarCaseEvidence> similar, int attempt, CancellationToken ct)
    {
        var key = BuildAttrCacheKey(testCase, failed, attempt);
        if (_cache.TryGetValue<AttributedResultDto>(key, out var cached))
        {
            _logger.LogInformation("命中归因结果缓存，跳过 LLM 调用（key={Key}）", key);
            return cached;
        }

        AttributedResultDto? attributed;
        try
        {
            attributed = await _ai.AttributeAsync(testCaseSummary, evidence, similar, ct);
        }
        catch (AIWorkerException ex)
        {
            _logger.LogInformation("归因调用失败，跳过闭环：{Message}", ex.Message);
            return null;
        }

        if (attributed is not null)
        {
            var ttl = TimeSpan.FromMinutes(Math.Max(1, _options.AttributionCacheTtlMinutes));
            _cache.Set(key, attributed, ttl);
        }
        return attributed;
    }

    /// <summary>
    /// 缓存键：用例指纹（步骤结构，编辑用例即失效）+ 错误签名（与 <see cref="FindSimilarAsync"/> 同源的错误前缀）
    /// + 尝试序（隔离同执行内多轮）。三者经 SHA256 折叠，避免键过长或含敏感信息。
    /// </summary>
    private static string BuildAttrCacheKey(TestCase testCase, List<ExecutionResult> failed, int attempt)
    {
        var fp = new StringBuilder();
        foreach (var s in testCase.Steps.OrderBy(x => x.StepOrder))
            fp.Append(s.ActionType).Append('|')
              .Append(s.Config?.Selector?.Value ?? "").Append('|')
              .Append(s.Config?.Value ?? "").Append('|')
              .Append(s.AIElementDescription ?? "").Append(';');

        var err = new StringBuilder();
        foreach (var f in failed.OrderBy(x => x.StepOrder))
        {
            var prefix = string.IsNullOrEmpty(f.ErrorMessage)
                ? ""
                : f.ErrorMessage[..Math.Min(80, f.ErrorMessage.Length)];
            err.Append(f.StepOrder).Append('|').Append(f.Status).Append('|').Append(prefix).Append(';');
        }

        return $"agent:attr:{Sha256(fp.ToString())}:{Sha256(err.ToString())}:{attempt}";
    }

    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private Task<global::AI.TestPlatform.Domain.Entities.Environment?> LoadEnvironmentAsync(Guid? envId, CancellationToken ct) =>
        envId is null
            ? Task.FromResult<global::AI.TestPlatform.Domain.Entities.Environment?>(null)
            : _db.Environments.FirstOrDefaultAsync(e => e.Id == envId.Value, ct);

    /// <summary>深拷贝步骤（含 Config/Selector/Headers），确保修改不落回任何被跟踪实体。</summary>
    private static List<TestStep> DeepCloneSteps(IReadOnlyList<TestStep> steps) =>
        steps.Select(s => new TestStep
        {
            Id = s.Id,
            TestCaseId = s.TestCaseId,
            StepOrder = s.StepOrder,
            ActionType = s.ActionType,
            Config = CloneConfig(s.Config),
            AIInstruction = s.AIInstruction,
            AIElementDescription = s.AIElementDescription,
            SharedGroupId = s.SharedGroupId,
        }).ToList();

    private static StepConfig CloneConfig(StepConfig c) => new()
    {
        Url = c.Url,
        Method = c.Method,
        Endpoint = c.Endpoint,
        Body = c.Body,
        Value = c.Value,
        Attribute = c.Attribute,
        Selector = c.Selector is null ? null : new SelectorConfig
        {
            Type = c.Selector.Type,
            Description = c.Selector.Description,
            Value = c.Selector.Value,
        },
        Headers = c.Headers?.Select(h => new HeaderEntry { Name = h.Name, Value = h.Value }).ToList(),
    };

    /// <summary>截断并把省略号算进上限（防 Postgres 22001：超列宽会整条写入失败）。</summary>
    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        if (max <= 3)
            return value[..max];
        return string.Concat(value.AsSpan(0, max - 3), "...");
    }
}
