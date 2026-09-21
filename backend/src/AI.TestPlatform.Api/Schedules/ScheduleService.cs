using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Application.Schedules;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Schedules;

/// <summary>
/// 定时任务执行服务：把到点的 Schedule 展开成一批 Pending 执行记录并唤醒执行器。
///
/// 多实例安全：到点判定用「比较并交换」（CAS）——把读到的 NextRunAt 作为 WHERE 条件，
/// 更新成功（影响 1 行）的那个实例才拥有本次执行权，其余实例自然落空，不会重复触发。
/// </summary>
public class ScheduleService
{
    /// <summary>单次定时任务最多创建的用例数，避免夜间批量把执行队列打爆</summary>
    public const int MaxCasesPerRun = 500;

    private readonly TestDbContext _db;
    private readonly ExecutionQueue _queue;
    private readonly ExecutionPlanner _planner;
    private readonly TestPlanService _planService;
    private readonly InAppNotificationService _notifications;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(TestDbContext db, ExecutionQueue queue, ExecutionPlanner planner,
        TestPlanService planService, InAppNotificationService notifications,
        ILogger<ScheduleService> logger)
    {
        _db = db;
        _queue = queue;
        _planner = planner;
        _planService = planService;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>扫描所有到点的任务并逐个 CAS 抢占执行；返回实际执行的 Schedule Id</summary>
    public async Task<List<Guid>> RunDueAsync(CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var due = await _db.Schedules.AsNoTracking()
            .Where(s => s.Enabled && s.NextRunAt != null && s.NextRunAt <= nowUtc)
            .OrderBy(s => s.NextRunAt)
            .Take(20)
            .Select(s => new { s.Id, s.CronExpression, s.NextRunAt })
            .ToListAsync(ct);
        if (due.Count == 0) return new List<Guid>();

        var executed = new List<Guid>();
        foreach (var item in due)
        {
            // Cron 按服务器本地时间解释（0 2 * * * = 本地 02:00），落库统一转 UTC
            var next = CronUtils.GetNextOccurrence(item.CronExpression, DateTime.Now);
            if (next is null)
            {
                await MarkErrorAsync(item.Id, "Cron 表达式非法，任务已停用", ct);
                continue;
            }

            // CAS：只有把 NextRunAt 从「我读到的那个值」推进成功的实例才执行
            var claimed = await _db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "Schedules" SET "NextRunAt" = {0}, "UpdatedAt" = now()
                WHERE "Id" = {1} AND "Enabled" AND "NextRunAt" = {2}
                """,
                new object[] { next.Value.ToUniversalTime(), item.Id, item.NextRunAt!.Value }, ct);
            if (claimed == 0) continue;

            await ExecuteAsync(item.Id, ct);
            executed.Add(item.Id);
        }
        return executed;
    }

    /// <summary>按 Id 执行一次（手动试跑也走这里）；返回创建的执行记录</summary>
    public async Task<ScheduleRunResult> ExecuteAsync(Guid scheduleId, CancellationToken ct)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null)
            return new ScheduleRunResult(scheduleId, 0, new List<Guid>(), null, "定时任务不存在");

        // 按执行范围类型分派，两个分支互斥：
        // 若两边都跑会让同一批用例跑两遍、报告数字翻倍。
        if (schedule.ScopeKind == ScheduleScopeKind.TestPlan)
            return await RunPlanScopeAsync(schedule, ct);

        try
        {
            var caseIds = await ResolveTestCaseIdsAsync(schedule, ct);
            if (caseIds.Count == 0)
            {
                await MarkErrorAsync(scheduleId, "没有匹配到可执行的测试用例", ct);
                return new ScheduleRunResult(scheduleId, 0, new List<Guid>(), schedule.NextRunAt, "没有匹配到可执行的测试用例");
            }

            var executionIds = new List<Guid>();
            var plan = await _planner.PlanAsync(
                caseIds, schedule.EnvironmentId, schedule.Browsers,
                schedule.ExpandDataSets, variables: null,
                TriggerType.Scheduled, $"定时任务：{schedule.Name}", triggeredById: null, ct: ct);
            _db.Executions.AddRange(plan.Executions);
            executionIds.AddRange(plan.Executions.Select(e => e.Id));

            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastCreatedCount = executionIds.Count;
            schedule.LastError = null;
            schedule.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            foreach (var id in executionIds)
                await _queue.EnqueueAsync(id, ct);

            _logger.LogInformation("定时任务 {Name}（{Id}）创建 {Count} 条执行",
                schedule.Name, schedule.Id, executionIds.Count);

            return new ScheduleRunResult(scheduleId, executionIds.Count, executionIds, schedule.NextRunAt, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时任务 {ScheduleId} 执行失败", scheduleId);
            await MarkErrorAsync(scheduleId, ex.Message, ct);
            return new ScheduleRunResult(scheduleId, 0, new List<Guid>(), null, ex.Message);
        }
    }

    /// <summary>
    /// 范围为「测试计划」时的执行：为每个选中的计划开一轮。
    ///
    /// 环境与浏览器按「定时任务显式设置时覆盖计划默认，留空则用计划默认」处理——
    /// 这样「同一计划、测试环境每晚跑、预发环境发版前跑」也是可表达的。
    /// 是否展开数据集始终用计划的：Schedule.ExpandDataSets 是 bool 无法表达「未设置」，
    /// 为一个边缘场景把它改成可空不值得。
    ///
    /// 计划不存在 / 已归档 / 已有进行中轮次 → **跳过并记日志**，既不算错误也不写 LastError：
    /// 定时任务常在夜间无人值守时跑，把「上一轮还没跑完」当错误刷进去只会制造噪音。
    /// </summary>
    private async Task<ScheduleRunResult> RunPlanScopeAsync(Schedule schedule, CancellationToken ct)
    {
        var planIds = schedule.TestPlanIds ?? [];
        if (planIds.Count == 0)
        {
            await MarkSkippedAsync(schedule, "执行范围为测试计划，但没有选择任何计划", ct);
            return new ScheduleRunResult(schedule.Id, 0, [], schedule.NextRunAt,
                Error: null, Message: "执行范围为测试计划，但没有选择任何计划");
        }

        var plans = await _db.TestPlans.AsNoTracking()
            .Where(p => planIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Status })
            .ToListAsync(ct);

        var roundIds = new List<Guid>();
        var messages = new List<string>();
        foreach (var planId in planIds)
        {
            var plan = plans.FirstOrDefault(p => p.Id == planId);
            if (plan is null)
            {
                messages.Add($"计划 {planId}：已不存在，跳过");
                continue;
            }
            if (plan.Status != TestPlanStatus.Active)
            {
                // 草稿/已完成/已归档的计划不该被定时任务自动开轮，否则会把「归档」变成摆设
                messages.Add($"{plan.Name}：当前状态为{StatusText(plan.Status)}，仅「进行中」的计划才会被定时触发");
                continue;
            }

            var (round, error) = await _planService.StartRoundAsync(plan.Id,
                new StartRoundRequest(
                    EnvironmentId: schedule.EnvironmentId,
                    Browsers: schedule.Browsers?.Count > 0 ? schedule.Browsers : null,
                    ExpandDataSets: null,
                    Variables: null),
                TriggerType.Scheduled, $"定时任务：{schedule.Name}", triggeredById: null, ct);

            if (round is null)
            {
                messages.Add($"{plan.Name}：{error}");
                _logger.LogInformation("定时任务 {Name} 触发计划 {Plan} 跳过：{Reason}",
                    schedule.Name, plan.Name, error);
                continue;
            }
            roundIds.Add(round.Id);
            messages.Add($"{plan.Name}：第 {round.RoundNo} 轮（{round.CreatedCount} 条执行）");
        }

        schedule.LastRunAt = DateTime.UtcNow;
        schedule.LastCreatedCount = roundIds.Count;
        schedule.LastError = null;
        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("定时任务 {Name}（{Id}）触发 {Count} 个计划轮次：{Detail}",
            schedule.Name, schedule.Id, roundIds.Count, string.Join("；", messages));

        return new ScheduleRunResult(schedule.Id, roundIds.Count, roundIds, schedule.NextRunAt,
            Error: null, Message: string.Join("；", messages));
    }

    /// <summary>被跳过的触发：只更新运行时间与提示，不写 LastError</summary>
    private async Task MarkSkippedAsync(Schedule schedule, string message, CancellationToken ct)
    {
        schedule.LastRunAt = DateTime.UtcNow;
        schedule.LastCreatedCount = 0;
        schedule.LastError = null;
        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("定时任务 {Name}（{Id}）本次跳过：{Message}",
            schedule.Name, schedule.Id, message);
    }

    private static string StatusText(TestPlanStatus status) => status switch
    {
        TestPlanStatus.Draft => "草稿",
        TestPlanStatus.Active => "进行中",
        TestPlanStatus.Completed => "已完成",
        _ => "已归档",
    };

    /// <summary>
    /// 解析任务覆盖的用例集合：指定用例 &gt; 模块/优先级过滤 &gt; 项目全部用例。
    /// 自动剔除不支持执行的 Mobile 用例，并限制单次上限。
    /// </summary>
    public async Task<List<Guid>> ResolveTestCaseIdsAsync(Schedule schedule, CancellationToken ct)
    {
        if (schedule.TestCaseIds is { Count: > 0 })
        {
            var explicitIds = schedule.TestCaseIds.Distinct().ToList();
            return await _db.TestCases.AsNoTracking()
                .Where(t => explicitIds.Contains(t.Id) && t.Type != TestType.Mobile)
                .Select(t => t.Id)
                .Take(MaxCasesPerRun)
                .ToListAsync(ct);
        }

        var query = _db.TestCases.AsNoTracking()
            .Where(t => t.ProjectId == schedule.ProjectId && t.Type != TestType.Mobile);
        if (!string.IsNullOrWhiteSpace(schedule.Module))
            query = query.Where(t => t.Module == schedule.Module);
        if (!string.IsNullOrWhiteSpace(schedule.Priority))
            query = query.Where(t => t.Priority == schedule.Priority);

        return await query.OrderBy(t => t.Name).Select(t => t.Id).Take(MaxCasesPerRun).ToListAsync(ct);
    }

    /// <summary>启用中的任务若缺少 NextRunAt（新建/改 cron 后未回填），补齐之</summary>
    public async Task<int> BackfillNextRunAsync(CancellationToken ct)
    {
        var pending = await _db.Schedules
            .Where(s => s.Enabled && s.NextRunAt == null)
            .ToListAsync(ct);
        foreach (var schedule in pending)
            schedule.NextRunAt = CronUtils.GetNextOccurrence(schedule.CronExpression)?.ToUniversalTime();
        if (pending.Count > 0) await _db.SaveChangesAsync(ct);
        return pending.Count;
    }

    private async Task MarkErrorAsync(Guid scheduleId, string error, CancellationToken ct)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null) return;
        schedule.LastError = error.Length > 500 ? error[..500] : error;
        schedule.LastRunAt = DateTime.UtcNow;
        // cron 非法时停用，避免每轮扫描反复报错
        if (error.Contains("Cron", StringComparison.OrdinalIgnoreCase)) schedule.Enabled = false;
        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // 定时任务常在夜间无人值守时跑，失败没人会当场看见 —— 这条最需要站内消息。
        // 接收人取项目的「测试负责人」：Schedule 表没有创建人字段（见实体定义），
        // 项目负责人是这张表唯一能推导出的责任人。
        var testOwnerId = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == schedule.ProjectId)
            .Select(p => p.TestOwnerId)
            .FirstOrDefaultAsync(ct);

        await _notifications.PushAsync(testOwnerId, new NotificationDraft(
            NotificationCategory.Schedule,
            $"定时任务执行失败：{schedule.Name}",
            Level: NotificationLevel.Error,
            Body: schedule.LastError,
            LinkUrl: "/schedules",
            LinkLabel: "查看定时任务",
            SourceType: "Schedule",
            SourceId: schedule.Id,
            ProjectId: schedule.ProjectId), ct);
    }
}
