using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Executions;

public static class ExecutionApiExtensions
{
    public static RouteGroupBuilder MapExecutionApi(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            CreateExecutionRequest request,
            IValidator<CreateExecutionRequest> validator,
            HttpContext http,
            TestDbContext db,
            ExecutionPlanner planner,
            ExecutionQueue queue,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var testCase = await db.TestCases.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TestCaseId, ct);
            if (testCase is null)
                return Results.NotFound();
            if (testCase.Type == TestType.Mobile)
                return Results.BadRequest(new { message = "当前仅支持 Web/API 用例执行" });

            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });
            if (!BrowserCatalog.IsRecognized(request.Browser))
                return Results.BadRequest(new { message = "浏览器只能是 chromium / firefox / webkit" });

            // 指定数据行时只取该行（不指定则取第一行为默认，避免一次点执行就展开成 N 条）
            var plan = await planner.PlanAsync(
                new[] { request.TestCaseId },
                request.EnvironmentId,
                request.Browser is null ? null : new List<string> { request.Browser },
                expandDataSets: request.DataSetRowIndex.HasValue,
                request.Variables,
                TriggerType.Manual, null, http.User.GetUserId(), ct: ct);

            var execution = plan.Executions.FirstOrDefault();
            if (execution is null)
                return Results.BadRequest(new { message = "没有可执行的用例" });
            if (request.DataSetRowIndex.HasValue)
                execution.DataSetRowIndex = request.DataSetRowIndex;
            // 未指定数据集行时清空（单次执行不绑定具体数据行，执行时用第一行变量）
            if (!request.DataSetRowIndex.HasValue)
            {
                execution.DataSetRowIndex = null;
                execution.DataSetRowLabel = null;
            }

            db.Executions.Add(execution);
            await db.SaveChangesAsync(ct);

            await queue.EnqueueAsync(execution.Id, ct);
            return Results.Accepted($"/api/executions/{execution.Id}", execution.ToSummaryDto(testCase.Name));
        }).WithPermission(Permission.RunExecutions).WithAudit("Execute", "Execution");

        // 批量执行：多用例一键回归，支持浏览器矩阵与数据行展开
        group.MapPost("/batch", async (
            BatchExecuteRequest request,
            IValidator<BatchExecuteRequest> validator,
            HttpContext http,
            TestDbContext db,
            ExecutionPlanner planner,
            ExecutionQueue queue,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            if (request.EnvironmentId.HasValue &&
                !await db.Environments.AsNoTracking().AnyAsync(e => e.Id == request.EnvironmentId.Value, ct))
                return Results.BadRequest(new { message = "环境不存在" });
            if (request.Browsers is { Count: > 0 } browsers && browsers.Any(b => !BrowserCatalog.IsRecognized(b)))
                return Results.BadRequest(new { message = "浏览器只能是 chromium / firefox / webkit" });

            var plan = await planner.PlanAsync(
                request.TestCaseIds, request.EnvironmentId, request.Browsers,
                request.ExpandDataSets, request.Variables,
                TriggerType.Manual, null, http.User.GetUserId(), ct: ct);

            if (plan.Executions.Count == 0)
                return Results.BadRequest(new { message = "没有有效的测试用例" });

            db.Executions.AddRange(plan.Executions);
            await db.SaveChangesAsync(ct);
            foreach (var execution in plan.Executions)
                await queue.EnqueueAsync(execution.Id, ct);

            return Results.Accepted("/api/executions",
                new BatchExecuteResult(plan.Executions.Count,
                    plan.Executions.Select(e => e.Id).ToList(),
                    plan.SkippedCaseIds,
                    plan.CasesWithoutData));
        }).WithPermission(Permission.RunExecutions).WithAudit("BatchExecute", "Execution");

        group.MapGet("/", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] Guid? testCaseId = null,
            [FromQuery] ExecutionStatus? status = null,
            // 近 N 天（仪表盘「近 7 天通过率」跳转用）
            [FromQuery] int? days = null,
            // 仅看进行中（Pending/Running，仪表盘「进行中执行」跳转用）
            [FromQuery] bool? runningOnly = null,
            // 套件运行 / 套件筛选（套件报告页用）
            [FromQuery] Guid? suiteRunId = null,
            [FromQuery] Guid? suiteId = null,
            // 浏览器筛选（多浏览器矩阵结果对照用）
            [FromQuery] string? browser = null,
            // 用例名称模糊搜索（执行记录列表页用）
            [FromQuery] string? testCaseName = null,
            // 开始时间范围（执行记录列表页用）
            [FromQuery] DateTime? dateFrom = null,
            [FromQuery] DateTime? dateTo = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            IQueryable<global::AI.TestPlatform.Domain.Entities.Execution> query = db.Executions.AsNoTracking();

            if (projectId.HasValue)
                query = query.Where(e => e.TestCase != null && e.TestCase.ProjectId == projectId.Value);
            if (testCaseId.HasValue)
                query = query.Where(e => e.TestCaseId == testCaseId.Value);
            if (status.HasValue)
                query = query.Where(e => e.Status == status.Value);
            if (days is > 0)
            {
                var since = DateTime.UtcNow.AddDays(-days.Value);
                query = query.Where(e => e.CreatedAt >= since);
            }
            if (runningOnly == true)
                query = query.Where(e => e.Status == ExecutionStatus.Pending ||
                                         e.Status == ExecutionStatus.Running);
            if (suiteRunId.HasValue)
                query = query.Where(e => e.SuiteRunId == suiteRunId.Value);
            if (suiteId.HasValue)
                query = query.Where(e => e.SuiteId == suiteId.Value);
            if (!string.IsNullOrWhiteSpace(browser))
            {
                var normalized = BrowserCatalog.Normalize(browser);
                query = query.Where(e => e.BrowserName == normalized);
            }
            if (!string.IsNullOrWhiteSpace(testCaseName))
            {
                var kw = testCaseName.Trim();
                query = query.Where(e => e.TestCase != null && EF.Functions.Like(e.TestCase.Name, $"%{kw}%"));
            }
            if (dateFrom.HasValue)
            {
                // Npgsql timestamptz 只吃 UTC；[FromQuery] 绑 URL "2026-09-22" → Kind=Unspecified → 必须转
                var fromUtc = DateTimeUtcHelper.SpecifyUtc(dateFrom.Value);
                query = query.Where(e => (e.StartedAt ?? e.CreatedAt) >= fromUtc);
            }
            if (dateTo.HasValue)
            {
                // 用户选 9/22 想包含当天所有执行 → exclusive 下一天零点
                var toExclusiveUtc = DateTimeUtcHelper.SpecifyUtc(dateTo.Value.Date.AddDays(1));
                query = query.Where(e => (e.StartedAt ?? e.CreatedAt) < toExclusiveUtc);
            }

            var total = await query.CountAsync(ct);
            // 投影查询：结果数走 COUNT 子查询，避免列表页加载全部步骤结果（含 StackTrace/StepSnapshot 大 JSONB）
            var items = await query
                .OrderByDescending(e => e.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new ExecutionSummaryDto(
                    e.Id,
                    e.TestCaseId,
                    db.TestCases.IgnoreQueryFilters()
                        .Where(t => t.Id == e.TestCaseId)
                        .Select(t => t.Name)
                        .FirstOrDefault() ?? "(用例已删除)",
                    e.Status,
                    e.TriggerType,
                    e.BrowserVersion,
                    e.StartedAt,
                    e.EndedAt,
                    e.DurationMs,
                    e.Results.Count,
                    e.CreatedAt,
                    e.AIDiagnosis,
                    e.EnvironmentSnapshot != null ? e.EnvironmentSnapshot.Name
                        : (e.Environment != null ? e.Environment.Name : null),
                    e.EnvironmentId,
                    e.TriggerSource,
                    e.CommitSha,
                    e.Branch,
                    e.BuildNumber,
                    e.BrowserName,
                    e.DataSetRowLabel,
                    e.SuiteId,
                    e.SuiteRunId,
                    // TraceUrl 落库的是历史格式（/traces/...），对外统一改发受权端点 URL（S1），
                    // 顺带把库里已存的旧格式一并"翻译"掉，存量执行无需迁移
                    e.TraceUrl == null ? null : $"/api/executions/{e.Id:N}/trace",
                    e.TraceSizeBytes,
                    // 编排：前置用例与跳过原因（列表状态列直接给出原因，否则「跳过」看着像脏数据）
                    e.DependsOnTestCaseId,
                    e.SkipReason,
                    // 录像：同样只对外发受权端点 URL
                    e.VideoUrl == null ? null : $"/api/executions/{e.Id:N}/video",
                    e.VideoSizeBytes,
                    // 所属项目名 —— 绕过 TestCase 软删过滤器，让已删用例的历史执行也能显示项目
                    db.TestCases.IgnoreQueryFilters()
                        .Where(t => t.Id == e.TestCaseId)
                        .Select(t => db.Projects.IgnoreQueryFilters()
                            .Where(p => p.Id == t.ProjectId)
                            .Select(p => p.Name)
                            .FirstOrDefault())
                        .FirstOrDefault(),
                    // M8 Agent 自愈：列表据此打「自愈通过」标记
                    e.AgentHealed))
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<ExecutionSummaryDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewExecutions);

        // 批量删除执行记录（连带步骤结果与截图）；进行中的执行会被跳过
        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            TestDbContext db,
            ScreenshotStorage screenshots,
            TraceStorage traces,
            VideoStorage videos,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ids = request.Ids.Distinct().ToList();
            var executions = await db.Executions
                .Include(e => e.TestCase)
                .Where(e => ids.Contains(e.Id))
                .ToListAsync(ct);

            var skipped = new List<BatchDeleteSkippedItem>();
            var deletedIds = new List<Guid>();
            foreach (var execution in executions)
            {
                if (execution.Status is ExecutionStatus.Pending or ExecutionStatus.Running)
                {
                    skipped.Add(new BatchDeleteSkippedItem(execution.Id,
                        execution.TestCase?.Name, "执行进行中，无法删除"));
                    continue;
                }
                db.Executions.Remove(execution);
                deletedIds.Add(execution.Id);
            }
            var found = executions.Select(e => e.Id).ToHashSet();
            foreach (var id in ids.Where(id => !found.Contains(id)))
                skipped.Add(new BatchDeleteSkippedItem(id, null, "执行记录不存在"));

            await db.SaveChangesAsync(ct);
            foreach (var id in deletedIds)
            {
                screenshots.Delete(id);
                // trace 包几 MB，留着就是磁盘垃圾
                traces.Delete(id);
                // 录像比 trace 更大，同理
                videos.Delete(id);
            }

            return Results.Ok(new BatchDeleteResultDto(deletedIds.Count, skipped));
        }).WithPermission(Permission.RunExecutions).WithAudit("BatchDelete", "Execution");

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var execution = await db.Executions.AsNoTracking()
                .Include(e => e.Results)
                .Include(e => e.TestCase)
                    // 详情页展示「所属项目」名称（透过用例取项目）
                    .ThenInclude(t => t!.Project)
                .Include(e => e.Environment)
                .FirstOrDefaultAsync(e => e.Id == id, ct);

            return execution is null
                ? Results.NotFound()
                : Results.Ok(execution.ToDetailDto(execution.TestCase?.Name ?? "(用例已删除)"));
        }).WithPermission(Permission.ViewExecutions);

        // M8 Agent 修复轨迹：执行详情页「Agent 修复轨迹」区展示每次尝试的归因 / 动作 / 结果。
        // 无自愈记录时返回空数组（前端不渲染该区）。
        group.MapGet("/{id:guid}/agent-attempts", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var attempts = await db.AgentAttempts.AsNoTracking()
                .Where(a => a.ExecutionId == id)
                .OrderBy(a => a.AttemptNumber)
                .ToListAsync(ct);
            return Results.Ok(attempts.Select(a => a.ToDto()).ToList());
        }).WithPermission(Permission.ViewExecutions);

        // M8 Agent 审批：采纳一次「需人工审批」的修复。
        // 采纳 = 把修复应用到**真实 TestStep**（与执行期只改副本相反）并持久化；破坏性/未实现的动作会被 FixActionApplier 拒绝。
        group.MapPost("/agent-attempts/{attemptId:guid}/approve", async (
            Guid attemptId, TestDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var attempt = await db.AgentAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null)
                return Results.NotFound();
            if (!attempt.NeedsApproval)
                return Results.BadRequest(new { message = "该尝试无需人工审批" });
            if (attempt.Approved is not null)
                return Results.BadRequest(new { message = "该尝试已处理" });

            var applied = 0;
            try
            {
                var fixes = string.IsNullOrWhiteSpace(attempt.AppliedActions)
                    ? new List<FixActionDto>()
                    // ⚠ 必须用 Web 选项反序列化：AppliedActions 是以 camelCase 序列化的，
                    // 用默认选项会因大小写不匹配而绑定失败（字段全 null）
                    : System.Text.Json.JsonSerializer.Deserialize<List<FixActionDto>>(
                        attempt.AppliedActions,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                      ?? new List<FixActionDto>();
                var ctx = await db.Executions.AsNoTracking()
                    .Where(e => e.Id == attempt.ExecutionId)
                    .Select(e => new
                    {
                        e.TestCaseId,
                        BaseUrl = e.TestCase != null ? e.TestCase.BaseUrl : null,
                    })
                    .FirstOrDefaultAsync(ct);
                if (ctx?.TestCaseId is { } cid && fixes.Count > 0)
                {
                    var steps = await db.TestSteps
                        .Where(s => s.TestCaseId == cid)
                        .OrderBy(s => s.StepOrder)
                        .ToListAsync(ct);
                    foreach (var fix in fixes)
                    {
                        // 采纳同样走 URL 同源校验（baseUrl）与全部安全校验
                        if (FixActionApplier.TryApply(steps, fix, out _, ctx.BaseUrl))
                            applied++;
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                applied = 0;
            }

            attempt.Approved = true;
            attempt.ApprovedBy = currentUser.Id;
            attempt.ApprovedAt = DateTime.UtcNow;
            attempt.Persisted = applied > 0;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { applied });
        }).WithPermission(Permission.ManageTestCases).WithAudit("Approve", "AgentAttempt");

        // M8 Agent 审批：驳回一次待审批修复（不改动用例）。
        group.MapPost("/agent-attempts/{attemptId:guid}/reject", async (
            Guid attemptId, TestDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
        {
            var attempt = await db.AgentAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);
            if (attempt is null)
                return Results.NotFound();
            if (attempt.Approved is not null)
                return Results.BadRequest(new { message = "该尝试已处理" });

            attempt.Approved = false;
            attempt.ApprovedBy = currentUser.Id;
            attempt.ApprovedAt = DateTime.UtcNow;
            attempt.Result = AgentAttemptResult.Rejected;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }).WithPermission(Permission.ManageTestCases).WithAudit("Reject", "AgentAttempt");

        // M8 Planner（Phase 3）：按该执行的目标 + 失败历史重新规划步骤序列。
        // 只返回建议（人工采纳），不自动改动用例——新步骤的落库属"采纳"流程。
        group.MapPost("/{id:guid}/replan", async (Guid id, IPlannerService planner, CancellationToken ct) =>
        {
            var plan = await planner.PlanAsync(id, ct);
            return Results.Ok(plan);
        }).WithPermission(Permission.ManageTestCases).WithAudit("Replan", "Execution");

        // M8 Agent 审批工作台：跨执行的待审批 / 已批准 / 已拒绝列表（status = pending|approved|rejected）
        group.MapGet("/agent-approvals", async (TestDbContext db, CancellationToken ct,
            [FromQuery] string? status = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var q = db.AgentAttempts.AsNoTracking().Where(a => a.NeedsApproval);
            q = status?.Trim().ToLowerInvariant() switch
            {
                "pending" => q.Where(a => a.Approved == null),
                "approved" => q.Where(a => a.Approved == true),
                "rejected" => q.Where(a => a.Approved == false),
                _ => q,
            };

            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(a => new AgentApprovalItemDto(
                    a.Id, a.ExecutionId,
                    a.Execution!.TestCase != null ? a.Execution.TestCase.Name : "(用例已删除)",
                    a.TargetStepOrder, (int)a.FixCategory, a.Confidence, a.FixSummary,
                    a.Approved, a.ApprovedBy, a.ApprovedAt, (int)a.Result, a.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(new PagedResult<AgentApprovalItemDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewExecutions);

        // 该执行里「已经转成缺陷」的步骤：执行详情页的「缺陷」列据此避免重复转单。
        // 两条路径都要覆盖——「创建新缺陷」写的是 Defect.FoundInExecutionId/FoundInStepOrder，
        // 而「认领已有缺陷」只写 DefectOccurrence 流水，所以必须并上复现流水。
        group.MapGet("/{id:guid}/defect-links", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var created = await db.Defects.AsNoTracking()
                .Where(d => d.FoundInExecutionId == id && d.FoundInStepOrder != null)
                .Select(d => new ExecutionDefectLinkDto(
                    d.FoundInStepOrder!.Value, d.Id, d.Title, d.Status))
                .ToListAsync(ct);

            var claimed = await db.DefectOccurrences.AsNoTracking()
                .Where(o => o.ExecutionId == id)
                .Select(o => new ExecutionDefectLinkDto(
                    o.StepOrder, o.DefectId, o.Defect.Title, o.Defect.Status))
                .ToListAsync(ct);

            // 同一步骤可能既被创建又被认领，按步骤去重——前端只关心"这一步有没有缺陷"
            var merged = created.Concat(claimed)
                .GroupBy(x => x.StepOrder)
                .Select(g => g.First())
                .OrderBy(x => x.StepOrder)
                .ToList();

            return Results.Ok(merged);
        }).WithPermission(Permission.ViewTestCases);

        // trace 下载（安全审查 S1）：原 /traces 静态目录注册在鉴权之前、完全绕过授权，
        // 而 trace 内含完整 DOM 快照与网络请求头。改为受权端点，与执行详情同一权限门槛。
        group.MapGet("/{id:guid}/trace", async (Guid id, TraceStorage traces, IArtifactStore store) =>
        {
            // 产物存储优先（MinIO/本地抽象），历史本地文件兜底——对象存储启用前的 trace 仍可下载
            var stream = await store.OpenReadAsync(TraceStorage.Key(id), CancellationToken.None)
                         ?? await Task.Run(() => File.Exists(traces.LegacyFilePath(id))
                             ? (Stream?)new FileStream(traces.LegacyFilePath(id), FileMode.Open,
                                 FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous)
                             : null, CancellationToken.None);
            if (stream is null)
                return Results.NotFound(new { message = "trace 不存在（仅失败执行的 trace 会保留）" });
            // enableRangeProcessing：几 MB 的包支持断点/拖动，trace viewer 直接消费
            return Results.File(stream, "application/zip", $"trace-{id:N}.zip",
                enableRangeProcessing: true);
        }).WithPermission(Permission.ViewExecutions);

        // 执行录像下载：与 trace 同一权限门槛（录像含页面内容，不能进公开静态目录）。
        // **必须支持 Range**：浏览器的 <video> 靠它做分段加载与拖动进度条，
        // 不支持的话表现为"放不出来"或"进度条拖不动"，而不是报错。
        group.MapGet("/{id:guid}/video", async (Guid id, IArtifactStore store) =>
        {
            var stream = await store.OpenReadAsync(VideoStorage.Key(id), CancellationToken.None);
            if (stream is null)
                return Results.NotFound(new { message = "录像不存在（仅失败执行的录像会保留）" });
            return Results.File(stream, VideoStorage.ContentType, $"video-{id:N}.webm",
                enableRangeProcessing: true);
        }).WithPermission(Permission.ViewExecutions);

        // 手动终止：Running 走本实例协作取消（正在跑的步骤立即中断），Pending 直接落 Canceled 终态
        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            TestDbContext db,
            IHubContext<Hubs.ExecutionHub> hub,
            CancellationToken ct) =>
        {
            var execution = await db.Executions.AsNoTracking()
                .Where(e => e.Id == id)
                .Select(e => new { e.Status })
                .FirstOrDefaultAsync(ct);
            if (execution is null)
                return Results.NotFound();
            if (execution.Status is not (ExecutionStatus.Pending or ExecutionStatus.Running))
                return Results.Conflict(new { message = "执行已结束，无需终止" });

            // Running 且在本实例：协作取消，执行器中断步骤后落 Canceled 终态并推送
            if (execution.Status == ExecutionStatus.Running && ExecutionWorker.RequestCancel(id))
                return Results.Accepted($"/api/executions/{id}");

            // Pending（或 Running 不在本实例）：条件更新直接落 Canceled。
            // 多实例场景下对 Running 强落 Canceled 是安全的：执行器的终态回写带 Status=1 条件，
            // 迟到的结果不会覆盖这里的结论；结果行由执行器照常入库保留证据。
            var affected = await db.Executions
                .Where(e => e.Id == id && e.Status == execution.Status)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(e => e.Status, ExecutionStatus.Canceled)
                    .SetProperty(e => e.EndedAt, DateTime.UtcNow)
                    .SetProperty(e => e.DurationMs, 0)
                    .SetProperty(e => e.HeartbeatAt, (DateTime?)null), ct);
            if (affected == 0)
                return Results.Conflict(new { message = "执行状态已变化，请刷新后重试" });

            try
            {
                await hub.Clients.Group(id.ToString("N")).SendAsync(
                    "StatusChanged", (int)ExecutionStatus.Canceled, ct);
            }
            catch
            {
                // 推送失败不影响终止结果，前端轮询兜底
            }
            return Results.Ok(new { message = "执行已终止" });
        }).WithPermission(Permission.RunExecutions).WithAudit("Cancel", "Execution");

        group.MapPost("/{id:guid}/diagnose", async (
            Guid id,
            TestDbContext db,
            DiagnosisService diagnosis,
            CancellationToken ct) =>
        {
            if (!await db.Executions.AsNoTracking().AnyAsync(e => e.Id == id, ct))
                return Results.NotFound();
            var diagnosed = await diagnosis.DiagnoseAsync(id, ct);
            return Results.Ok(new { diagnosed });
        }).WithPermission(Permission.ViewExecutions);

        return group;
    }
}

/// <summary>
/// Npgsql timestamptz 只接受 DateTime.Kind=Utc。
/// [FromQuery] 绑定 URL "2026-09-22" 时 Kind=Unspecified → 本辅助做 SpecifyKind(Local) + ToUniversalTime。
/// </summary>
internal static class DateTimeUtcHelper
{
    public static DateTime SpecifyUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
    };
}

/// <summary>
/// 执行里某一步骤已关联的缺陷（GET /executions/{id}/defect-links）。
/// 前端「缺陷」列据此把已转单的步骤显示成缺陷链接，而不是再给一个「转缺陷」入口。
/// </summary>
public sealed record ExecutionDefectLinkDto(int StepOrder, Guid DefectId, string DefectTitle, DefectStatus Status);
