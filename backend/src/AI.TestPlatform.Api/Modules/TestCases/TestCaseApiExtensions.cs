using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.TestCases;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Application.Executions;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.TestCases;

public static class TestCaseApiExtensions
{
    public static RouteGroupBuilder MapTestCaseApi(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null,
            [FromQuery] CaseReviewStatus? reviewStatus = null,
            [FromQuery] string? search = null,
            [FromQuery] string? module = null,
            // 按关联需求筛选：需求覆盖页点「关联用例」数字跳过来时带上
            [FromQuery] Guid? requirementId = null,
            // 仅看被标记为不稳定的用例（flake 隔离视图）
            [FromQuery] bool? flakyOnly = null,
            // 按「最近执行结果」筛选。口径与列表那一列**必须同源**
            [FromQuery] CaseExecFilter? execState = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 ? 20 : pageSize > 100 ? 100 : pageSize;

            var query = db.TestCases.AsNoTracking();
            if (projectId.HasValue)
                query = query.Where(t => t.ProjectId == projectId.Value);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t => t.Name.Contains(search) ||
                                         (t.CaseCode != null && t.CaseCode.Contains(search)));
            if (!string.IsNullOrWhiteSpace(module))
                query = query.Where(t => t.Module == module);
            if (requirementId.HasValue)
                query = query.Where(t => t.RequirementId == requirementId.Value);
            if (execState.HasValue && !Enum.IsDefined(execState.Value))
                // 别指望绑定层拦住：Enum.TryParse 会**接受** "99" 这种未定义数值，
                // 于是筛选落到空状态集合、静默返回 0 条 —— 比报错难查得多
                return Results.BadRequest(new { message = "执行状态筛选值无效" });

            if (flakyOnly == true)
                query = query.Where(t => t.IsFlaky);
            if (execState.HasValue)
                query = WhereLatestStatus(query, execState.Value);
            if (reviewStatus.HasValue)
                query = query.Where(t => t.ReviewStatus == reviewStatus.Value);

            var total = await query.CountAsync(ct);
            var entities = await query
                .Include(t => t.Requirement)
                // 列表要显示所属项目名；不 Include 的话下面 t.Project.Name 会 NRE
                .Include(t => t.Project)
                .OrderByDescending(t => t.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var latestExec = await LoadLatestExecutionAsync(db, entities.Select(t => t.Id).ToList(), ct);

            var items = entities.Select(t =>
            {
                var hasExec = latestExec.TryGetValue(t.Id, out var exec);
                return new TestCaseSummaryDto(
                    t.Id, t.ProjectId, t.Name, t.Type, t.Description, t.AIGenerated,
                    t.Browser, t.Timeout, t.RetryCount, t.Version, t.Status,
                    t.CreatedAt, t.UpdatedAt, t.BaseUrl, t.FailFast,
                    t.CaseCode, t.Module, t.Priority, t.IsFlaky, t.FlakeRate,
                    t.VisualEnabled, t.VisualThreshold, t.VisualIgnoreRegions, t.CustomFields, t.DataSetId,
                    t.ReviewStatus, t.ReviewedAt, t.ReviewNote,
                    t.ReviewedBy != null ? t.ReviewedBy.DisplayName : null,
                    t.RequirementId, t.Requirement?.Title, t.Project.Name,
                    hasExec ? exec.Status : null,
                    hasExec ? exec.At : null);
            }).ToList();

            return Results.Ok(new PagedResult<TestCaseSummaryDto>(items, total, page, pageSize));
        }).WithPermission(Permission.ViewTestCases);

        // 模块列表：供列表页筛选下拉使用
        group.MapGet("/modules", async (
            TestDbContext db,
            CancellationToken ct,
            [FromQuery] Guid? projectId = null) =>
        {
            var query = db.TestCases.AsNoTracking()
                .Where(t => t.Module != null && t.Module != "");
            if (projectId.HasValue)
                query = query.Where(t => t.ProjectId == projectId.Value);

            var modules = await query
                .GroupBy(t => t.Module!)
                .Select(g => new { Module = g.Key, Count = g.Count() })
                .OrderBy(m => m.Module)
                .ToListAsync(ct);

            return Results.Ok(modules);
        }).WithPermission(Permission.ViewTestCases);

        // 下载导入模板（填写说明 + 示例模块）
        group.MapGet("/import-template", () =>
        {
            var bytes = TestCaseTemplateBuilder.Build();
            return Results.File(bytes, TestCaseTemplateBuilder.ContentType, TestCaseTemplateBuilder.FileName);
        }).WithPermission(Permission.ViewTestCases);

        // Excel 用例导入（multipart/form-data：file + projectId + useAi + overwrite + baseUrl）
        group.MapPost("/import", async (
            HttpRequest request,
            TestDbContext db,
            TestCaseImportService importService,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "请以 multipart/form-data 上传 Excel 文件" });

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { message = "未收到上传文件（字段名应为 file）" });
            if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "仅支持 .xlsx 格式（请使用模板文件）" });

            if (!Guid.TryParse(form["projectId"], out var projectId))
                return Results.BadRequest(new { message = "缺少有效的 projectId" });
            if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
                return Results.BadRequest(new { message = "项目不存在" });

            var useAi = !bool.TryParse(form["useAi"], out var ai) || ai;
            var overwrite = bool.TryParse(form["overwrite"], out var ow) && ow;
            var baseUrl = form["baseUrl"].ToString().Trim();

            // 可选：导入的用例自动加入该测试计划范围（必须属于同一项目）
            Guid? testPlanId = Guid.TryParse(form["testPlanId"], out var tp) && tp != Guid.Empty ? tp : null;
            if (testPlanId is { } planId)
            {
                var plan = await db.TestPlans.AsNoTracking()
                    .Where(p => p.Id == planId)
                    .Select(p => new { p.ProjectId })
                    .FirstOrDefaultAsync(ct);
                if (plan is null)
                    return Results.BadRequest(new { message = "所选测试计划不存在" });
                if (plan.ProjectId != projectId)
                    return Results.BadRequest(new { message = "测试计划与所选项目不匹配" });
            }

            await using var stream = file.OpenReadStream();
            try
            {
                var result = await importService.ImportAsync(
                    stream, projectId, useAi,
                    string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl,
                    overwrite, testPlanId, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).WithPermission(Permission.ManageTestCases).WithAudit("Import", "TestCase", captureBody: false);

        group.MapGet("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.AsNoTracking()
                .Include(t => t.Steps.OrderBy(s => s.StepOrder)).ThenInclude(s => s.SharedGroup)
                // 详情页展示「所属项目」名称
                .Include(t => t.Project)
                .Include(t => t.ReviewedBy)
                .FirstOrDefaultAsync(e => e.Id == id, ct);

            return testCase is null ? Results.NotFound() : Results.Ok(testCase.ToDto());
        }).WithPermission(Permission.ViewTestCases);

        group.MapPost("/", async (
            CreateTestCaseRequest request,
            IValidator<CreateTestCaseRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var projectExists = await db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists)
                return Results.BadRequest(new { message = "项目不存在" });
            if (!IsValidIgnoreRegions(request.VisualIgnoreRegions))
                return Results.BadRequest(new { message = "视觉忽略区域格式无效：应为 [{x,y,w,h}] 数组，坐标为 0~100 的百分比" });
            var customFieldError = await ValidateCustomFieldsAsync(request.ProjectId, request.CustomFields, db, ct);
            if (customFieldError != null)
                return Results.BadRequest(new { message = customFieldError });

            // 网络规则在**这里**校验并规范化：留到执行期才炸的话，
            // 用户拿到的是 Playwright 的底层异常，看不出是哪条规则、哪个字段写错了
            string? networkRules;
            try
            {
                networkRules = NetworkRuleSet.Normalize(request.NetworkRules);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            var testCase = new TestCase
            {
                ProjectId = request.ProjectId,
                Name = request.Name,
                Type = request.Type,
                Description = request.Description,
                Browser = request.Browser,
                Timeout = request.Timeout,
                RetryCount = request.RetryCount,
                VisualEnabled = request.VisualEnabled,
                VisualThreshold = request.VisualThreshold ?? 0.01,
                VisualIgnoreRegions = request.VisualIgnoreRegions,
                CustomFields = request.CustomFields,
                DataSetId = request.DataSetId,
                RequirementId = request.RequirementId,
                NetworkRules = networkRules,
                BaseUrl = request.BaseUrl,
                FailFast = request.FailFast,
                CaseCode = request.CaseCode,
                Module = request.Module,
                SourceSteps = request.SourceSteps,
                ExpectedResult = request.ExpectedResult,
                Priority = request.Priority,
                Steps = request.Steps.Select(s => new TestStep
                {
                    StepOrder = s.StepOrder,
                    ActionType = s.ActionType,
                    Config = s.Config,
                    AIInstruction = s.AIInstruction,
                    AIElementDescription = s.AIElementDescription,
                    SharedGroupId = s.SharedGroupId,
                    SharedVariables = s.SharedVariables,
                }).ToList(),
            };
            db.TestCases.Add(testCase);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/testcases/{testCase.Id}", testCase.ToDto());
        }).WithPermission(Permission.ManageTestCases).WithAudit("Create", "TestCase");

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateTestCaseRequest request,
            IValidator<UpdateTestCaseRequest> validator,
            TestDbContext db,
            TestCaseVersionService versions,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var testCase = await db.TestCases
                .Include(t => t.Steps.OrderBy(s => s.StepOrder)).ThenInclude(s => s.SharedGroup)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound();

            // 与创建一致：网络规则先校验再规范化，非法值直接 400 而不是留到执行期
            string? networkRules;
            try
            {
                networkRules = NetworkRuleSet.Normalize(request.NetworkRules);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }

            // 先按请求算出"改完之后长什么样"，据此判断内容有没有真变化，
            // 变了就把**改之前**的内容存成第 Version 版快照（详情见 TestCaseVersionService）
            var incoming = testCase.ToSnapshot() with
            {
                Name = request.Name,
                Description = request.Description,
                Browser = request.Browser,
                Timeout = request.Timeout,
                RetryCount = request.RetryCount,
                VisualEnabled = request.VisualEnabled,
                VisualThreshold = request.VisualThreshold ?? testCase.VisualThreshold,
                VisualIgnoreRegions = request.VisualIgnoreRegions,
                CustomFields = request.CustomFields,
                DataSetId = request.DataSetId,
                RequirementId = request.RequirementId,
                BaseUrl = request.BaseUrl,
                FailFast = request.FailFast,
                CaseCode = request.CaseCode,
                Module = request.Module,
                SourceSteps = request.SourceSteps,
                ExpectedResult = request.ExpectedResult,
                Priority = request.Priority,
                NetworkRules = networkRules,
            };
            // 内容有实质变更时，已通过的评审自动回待评审——评审结论绑定内容版本，
            // 否则"评审通过后偷偷改步骤"就能绕过评审
            var contentChanged = await versions.RecordBeforeChangeAsync(testCase, incoming, ct);
            if (contentChanged && testCase.ReviewStatus == CaseReviewStatus.Approved)
                testCase.ReviewStatus = CaseReviewStatus.Pending;

            testCase.Name = request.Name;
            testCase.Description = request.Description;
            testCase.Status = request.Status;
            testCase.Browser = request.Browser;
            testCase.Timeout = request.Timeout;
            testCase.RetryCount = request.RetryCount;
            testCase.VisualEnabled = request.VisualEnabled;
            testCase.VisualThreshold = request.VisualThreshold ?? testCase.VisualThreshold;
            if (!IsValidIgnoreRegions(request.VisualIgnoreRegions))
                return Results.BadRequest(new { message = "视觉忽略区域格式无效：应为 [{x,y,w,h}] 数组，坐标为 0~100 的百分比" });
            var customFieldError = await ValidateCustomFieldsAsync(testCase.ProjectId, request.CustomFields, db, ct);
            if (customFieldError != null)
                return Results.BadRequest(new { message = customFieldError });
            testCase.VisualIgnoreRegions = request.VisualIgnoreRegions;
            testCase.CustomFields = request.CustomFields;
            testCase.DataSetId = request.DataSetId;
            testCase.RequirementId = request.RequirementId;
            testCase.BaseUrl = request.BaseUrl;
            testCase.FailFast = request.FailFast;
            testCase.CaseCode = request.CaseCode;
            testCase.Module = request.Module;
            testCase.SourceSteps = request.SourceSteps;
            testCase.ExpectedResult = request.ExpectedResult;
            testCase.Priority = request.Priority;
            // null = 清空规则（编辑表单每次提交的都是完整规则集，与 RequirementId 同语义）
            testCase.NetworkRules = networkRules;
            testCase.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(testCase.ToDto());
        }).WithPermission(Permission.ManageTestCases).WithAudit("Update", "TestCase");

        group.MapPut("/{id:guid}/steps", async (
            Guid id,
            UpdateTestCaseStepsRequest request,
            IValidator<UpdateTestCaseStepsRequest> validator,
            TestDbContext db,
            TestCaseVersionService versions,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var testCase = await db.TestCases
                .Include(t => t.Steps.OrderBy(s => s.StepOrder)).ThenInclude(s => s.SharedGroup)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound();

            // 步骤是改动的重头戏，尤其要记版本。
            // incoming 的步骤一定用 ToStepSnapshot 构造（和 ToSnapshot 共用序列化选项），
            // 否则签名对不上、每次保存都多一版
            var incomingSteps = request.Steps.OrderBy(s => s.StepOrder)
                .Select(s => TestCaseSnapshotExtensions.ToStepSnapshot(
                    s.StepOrder, s.ActionType, s.Config, s.AIInstruction, s.AIElementDescription,
                    s.SharedGroupId, s.SharedVariables))
                .ToList();
            await versions.RecordBeforeChangeAsync(
                testCase, testCase.ToSnapshot() with { Steps = incomingSteps }, ct);

            db.TestSteps.RemoveRange(testCase.Steps);
            testCase.Steps = request.Steps.Select(s => new TestStep
            {
                StepOrder = s.StepOrder,
                ActionType = s.ActionType,
                Config = s.Config,
                AIInstruction = s.AIInstruction,
                AIElementDescription = s.AIElementDescription,
                SharedGroupId = s.SharedGroupId,
                SharedVariables = s.SharedVariables,
            }).ToList();
            testCase.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(testCase.ToDto());
        }).WithPermission(Permission.ManageTestCases).WithAudit("UpdateSteps", "TestCase");

        // 批量删除（软删除：打 DeletedAt 时间戳，查询过滤器负责隐藏）
        group.MapPost("/batch-delete", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ids = request.Ids.Distinct().ToList();
            var cases = await db.TestCases.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var testCase in cases)
            {
                testCase.DeletedAt = now;
                testCase.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);

            var found = cases.Select(t => t.Id).ToHashSet();
            var skipped = ids.Where(id => !found.Contains(id))
                .Select(id => new BatchDeleteSkippedItem(id, null, "用例不存在或已删除"))
                .ToList();

            return Results.Ok(new BatchDeleteResultDto(cases.Count, skipped));
        }).WithPermission(Permission.ManageTestCases).WithAudit("BatchDelete", "TestCase");

        // 批量编辑：字段白名单（模块/优先级/状态/项目/需求），null 不改。逐条记版本快照——
        // "只改了模块没记版本"和单条编辑是同一种静默丢失，批量不能豁免。
        group.MapPost("/batch-update", async (
            BatchUpdateRequest request,
            IValidator<BatchUpdateRequest> validator,
            TestDbContext db,
            TestCaseVersionService versions,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            // 项目/需求的存在性与归属校验（一次加载，循环里复用）
            if (request.ProjectId is not null)
            {
                var projectExists = await db.Projects.AsNoTracking()
                    .AnyAsync(p => p.Id == request.ProjectId, ct);
                if (!projectExists)
                    return Results.BadRequest(new { message = "目标项目不存在" });
            }

            Requirement? targetRequirement = null;
            if (request.RequirementId is not null)
            {
                targetRequirement = await db.Requirements.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == request.RequirementId, ct);
                if (targetRequirement is null)
                    return Results.BadRequest(new { message = "目标需求不存在" });
                // 需求必须属于目标项目：显式传了项目就按它校验；没传项目则按需求自己的项目
                var anchorProjectId = request.ProjectId ?? targetRequirement.ProjectId;
                if (targetRequirement.ProjectId != anchorProjectId)
                    return Results.BadRequest(new { message = "需求不属于所选项目" });
            }

            var ids = request.Ids.Distinct().ToList();
            var cases = await db.TestCases
                .Include(t => t.Steps.OrderBy(s => s.StepOrder)).ThenInclude(s => s.SharedGroup)
                .Where(t => ids.Contains(t.Id))
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            var skipped = new List<BatchUpdateSkippedItem>();
            var updated = 0;

            foreach (var testCase in cases)
            {
                // 需求归属逐条校验：勾选的用例可能跨项目（列表不强制单项目筛选），
                // 需求对不上号的用例跳过而不是整体失败——其余用例照常应用
                if (targetRequirement is not null && testCase.ProjectId != targetRequirement.ProjectId)
                {
                    skipped.Add(new BatchUpdateSkippedItem(testCase.Id, testCase.Name, "需求所属项目与用例当前项目不一致"));
                    continue;
                }

                // 项目迁移且未显式指定新需求 → 清空旧需求，避免留下别的项目的悬空关联
                var projectChanging = request.ProjectId is not null && request.ProjectId != testCase.ProjectId;
                var effectiveRequirementId = targetRequirement is not null ? targetRequirement.Id
                    : projectChanging ? null
                    : testCase.RequirementId;

                // 与单条编辑同一套口径：先算"改完之后长什么样"，变了才记快照
                // 快照有意不含 Status：状态是流转不是内容（与单条编辑同口径），批量改状态走审计记录
                if (request.Module != null || request.Priority != null
                    || testCase.RequirementId != effectiveRequirementId)
                {
                    var incoming = testCase.ToSnapshot() with
                    {
                        Module = request.Module ?? testCase.Module,
                        Priority = request.Priority ?? testCase.Priority,
                        RequirementId = effectiveRequirementId,
                    };
                    await versions.RecordBeforeChangeAsync(testCase, incoming, ct);
                }

                if (request.Module != null) testCase.Module = request.Module.Trim();
                if (request.Priority != null) testCase.Priority = request.Priority.Trim();
                if (request.Status != null) testCase.Status = request.Status.Value;
                if (request.ProjectId != null) testCase.ProjectId = request.ProjectId.Value;
                if (targetRequirement is not null || projectChanging)
                    testCase.RequirementId = effectiveRequirementId;
                testCase.UpdatedAt = now;
                updated++;
            }

            await db.SaveChangesAsync(ct);

            var found = cases.Select(t => t.Id).ToHashSet();
            skipped.AddRange(ids.Where(id => !found.Contains(id))
                .Select(id => new BatchUpdateSkippedItem(id, null, "用例不存在或已删除")));

            return Results.Ok(new BatchUpdateResult(updated, skipped));
        }).WithPermission(Permission.ManageTestCases).WithAudit("BatchUpdate", "TestCase");

        group.MapDelete("/{id:guid}", async (Guid id, TestDbContext db, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound();

            // 软删除：打 DeletedAt，查询过滤器自动隐藏（状态字段保持不动，删除不是状态）
            testCase.DeletedAt = DateTime.UtcNow;
            testCase.UpdatedAt = testCase.DeletedAt.Value;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageTestCases).WithAudit("Delete", "TestCase");

        // ------------------------------ 评审流转（方案 A：标记层）
        // 提交评审：None/Rejected → Pending。通知项目测试负责人（有邮箱时）。
        group.MapPost("/{id:guid}/submit-review", async (
            Guid id, TestDbContext db, HttpContext http,
            NotificationService notifications, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.Include(t => t.Project)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound(new { message = "用例不存在" });
            if (testCase.ReviewStatus is CaseReviewStatus.Pending)
                return Results.Json(new { message = "该用例已在评审中，无需重复提交" }, statusCode: 409);

            var userId = http.User.GetUserId();
            testCase.ReviewStatus = CaseReviewStatus.Pending;
            testCase.ReviewSubmittedById = userId;
            testCase.ReviewSubmittedAt = DateTime.UtcNow;
            testCase.ReviewedById = null;
            testCase.ReviewedAt = null;
            testCase.ReviewNote = null;
            testCase.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var submitterName = http.User.Identity?.Name ?? "同事";
            var reviewer = testCase.Project?.TestOwnerId is { } ownerId
                ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId, ct)
                : null;
            if (reviewer?.Email is { } email && !string.IsNullOrWhiteSpace(email) && reviewer.Id != userId)
            {
                await notifications.NotifyReviewEventAsync(
                    new[] { email },
                    $"【AI 测试平台】{submitterName} 提交了用例评审：「{testCase.Name}」",
                    $"{submitterName} 提交了用例「{testCase.Name}」的评审申请，请登录平台处理。");
            }
            return Results.Ok(new { message = "已提交评审" });
        }).WithPermission(Permission.ManageTestCases).WithAudit("SubmitReview", "TestCase");

        // 批准 / 驳回：Pending → Approved/Rejected。驳回必须给意见。通知提交人。
        group.MapPost("/{id:guid}/review", async (
            Guid id, ReviewActionRequest request,
            TestDbContext db, HttpContext http,
            NotificationService notifications, CancellationToken ct) =>
        {
            var testCase = await db.TestCases.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound(new { message = "用例不存在" });
            if (testCase.ReviewStatus is not CaseReviewStatus.Pending)
                return Results.Json(new { message = "该用例不在待评审状态" }, statusCode: 409);
            if (request.Action is not ("approve" or "reject"))
                return Results.BadRequest(new { message = "action 只能是 approve 或 reject" });

            var note = request.Note?.Trim();
            if (request.Action == "reject" && string.IsNullOrWhiteSpace(note))
                return Results.BadRequest(new { message = "驳回时必须填写评审意见" });

            var userId = http.User.GetUserId();
            testCase.ReviewStatus = request.Action == "approve"
                ? CaseReviewStatus.Approved : CaseReviewStatus.Rejected;
            testCase.ReviewedById = userId;
            testCase.ReviewedAt = DateTime.UtcNow;
            testCase.ReviewNote = note;
            testCase.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // 通知提交人（有邮箱时）；评审人 @ 自己（自己提交自己批）不发
            if (testCase.ReviewSubmittedById is { } submitterId && submitterId != userId)
            {
                var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == submitterId, ct);
                if (submitter?.Email is { } email && !string.IsNullOrWhiteSpace(email))
                {
                    var reviewerName = http.User.Identity?.Name ?? "评审人";
                    var verb = request.Action == "approve" ? "已通过" : "已驳回";
                    var noteText = string.IsNullOrWhiteSpace(note) ? "" : $"评审意见：{note}";
                    await notifications.NotifyReviewEventAsync(
                        new[] { email },
                        $"【AI 测试平台】用例「{testCase.Name}」的评审{verb}",
                        $"你提交的用例「{testCase.Name}」评审{verb}。{noteText}");
                }
            }
            return Results.Ok(new { message = request.Action == "approve" ? "已批准" : "已驳回" });
        }).WithPermission(Permission.ManageTestCases).WithAudit("Review", "TestCase");

        // 手动设置 / 解除「不稳定」标记（自动识别之外的人工干预，如已修复或确认是环境抖动）
        group.MapPost("/{id:guid}/flake", async (
            Guid id,
            SetFlakeRequest request,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var testCase = await db.TestCases.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (testCase is null)
                return Results.NotFound();

            testCase.IsFlaky = request.IsFlaky;
            testCase.FlakeRate = request.IsFlaky ? 1 : 0;
            testCase.FlakeCheckedAt = DateTime.UtcNow;
            testCase.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { testCase.Id, testCase.IsFlaky, testCase.FlakeRate });
        }).WithPermission(Permission.ManageTestCases).WithAudit("ToggleFlake", "TestCase");

        // 批量解除不稳定标记（修复后重置统计基准）
        group.MapPost("/reset-flake", async (
            BatchDeleteRequest request,
            IValidator<BatchDeleteRequest> validator,
            TestDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var ids = request.Ids.Distinct().ToList();
            var cases = await db.TestCases.Where(t => ids.Contains(t.Id) && t.IsFlaky).ToListAsync(ct);
            foreach (var testCase in cases)
            {
                testCase.IsFlaky = false;
                testCase.FlakeRate = 0;
                testCase.FlakeCheckedAt = DateTime.UtcNow;
                testCase.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { reset = cases.Count });
        }).WithPermission(Permission.ManageTestCases).WithAudit("ToggleFlake", "TestCase");

        return group;
    }

    /// <summary>
    /// 按「最近一次执行的状态」过滤。
    ///
    /// ⚠ **排序必须与下面 <see cref="LoadLatestExecutionAsync"/> 的
    /// `DISTINCT ON ("TestCaseId") ... ORDER BY "CreatedAt" DESC` 完全一致**。
    /// 列表那一列显示的就是同一件事：两处一旦分叉，就会出现
    /// 「列里写着通过、却被『失败』筛出来」——用户会直接不信这个筛选，而且极难查。
    /// 改这里时请一并改那边（反之亦然）。
    /// </summary>
    private static IQueryable<TestCase> WhereLatestStatus(IQueryable<TestCase> query, CaseExecFilter filter)
    {
        // 「未执行」= 一条执行记录都没有。不能写成"最近一次状态 == null"，
        // 那样语义上等价但会多一次子查询，也没有更清楚
        if (filter == CaseExecFilter.Never)
            return query.Where(t => !t.Executions.Any());

        var wanted = StatusesOf(filter);
        return query.Where(t => wanted.Contains(
            t.Executions.OrderByDescending(e => e.CreatedAt)
                .Select(e => (ExecutionStatus?)e.Status)
                .FirstOrDefault()));
    }

    private static ExecutionStatus?[] StatusesOf(CaseExecFilter filter) => filter switch
    {
        // 「执行中」是一个桶：排队中(Pending)和正在跑(Running)对"哪些还在跑"这个问题没区别
        CaseExecFilter.Running => new ExecutionStatus?[] { ExecutionStatus.Pending, ExecutionStatus.Running },
        CaseExecFilter.Passed => new ExecutionStatus?[] { ExecutionStatus.Passed },
        CaseExecFilter.Failed => new ExecutionStatus?[] { ExecutionStatus.Failed },
        // Error 与 Failed 分开：前者是用例/环境跑挂了，后者是断言不通过，归因完全不同
        CaseExecFilter.Error => new ExecutionStatus?[] { ExecutionStatus.Error },
        CaseExecFilter.Skipped => new ExecutionStatus?[] { ExecutionStatus.Skipped },
        _ => Array.Empty<ExecutionStatus?>(),
    };

    /// <summary>
    /// 批量取每个用例「最近一次执行」的状态与时间，供列表的「最近执行结果」列使用。
    ///
    /// 用 PostgreSQL 的 `DISTINCT ON` 一次算完，而不是把用例的执行历史拉回内存再分组：
    /// 计划列表那种"内存聚合"是有边界的（一次轮次的规模），而**单个用例的执行历史没有边界**，
    /// 跑得越久的用例行越多，翻到某一页就可能拉回上万行——那是会随使用时间恶化的写法。
    /// </summary>
    private static async Task<Dictionary<Guid, (ExecutionStatus Status, DateTime At)>>
        LoadLatestExecutionAsync(TestDbContext db, List<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return new();

        var rows = await db.Executions
            .FromSql($"""
                SELECT DISTINCT ON ("TestCaseId") *
                FROM "Executions"
                WHERE "TestCaseId" = ANY({caseIds})
                ORDER BY "TestCaseId", "CreatedAt" DESC
                """)
            .AsNoTracking()
            .Select(e => new { CaseId = e.TestCaseId!.Value, e.Status, e.CreatedAt })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.CaseId, r => (r.Status, r.CreatedAt));
    }

    /// <summary>视觉忽略区域的 JSON 校验：必须是数组且元素含数值 x/y/w/h。非法直接 400（手工配置的坏数据不该静默吞掉）。</summary>

    /// <summary>
    /// 扩展字段值校验：jsonb 对象，键必须是本项目已定义的字段 Id，值按字段类型轻校验。
    /// 定义已删除的键直接忽略（残留无害）。返回错误消息，null = 合法。
    /// </summary>
    private static async Task<string?> ValidateCustomFieldsAsync(
        Guid projectId, string? json, TestDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        System.Text.Json.JsonElement root;
        try
        {
            root = System.Text.Json.JsonDocument.Parse(json).RootElement;
        }
        catch (System.Text.Json.JsonException)
        {
            return "扩展字段格式无效：应为 JSON 对象";
        }
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            return "扩展字段格式无效：应为 JSON 对象";

        var defs = await db.CustomFieldDefs.AsNoTracking()
            .Where(f => f.ProjectId == projectId)
            .ToDictionaryAsync(f => f.Id, f => f, ct);

        foreach (var prop in root.EnumerateObject())
        {
            if (!Guid.TryParse(prop.Name, out var defId) || !defs.TryGetValue(defId, out var def))
                continue; // 未知键（定义已删）静默忽略
            if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String)
                return $"扩展字段「{def.Name}」的值必须是字符串";
            var value = prop.Value.GetString() ?? "";
            if (value.Length > 500)
                return $"扩展字段「{def.Name}」的值不能超过 500 个字符";
            if (def.FieldType == CustomFieldType.Number && !double.TryParse(value, out _))
                return $"扩展字段「{def.Name}」必须是数字";
            if (def.FieldType == CustomFieldType.Select)
            {
                var options = string.IsNullOrWhiteSpace(def.Options)
                    ? Array.Empty<string>()
                    : System.Text.Json.JsonSerializer.Deserialize<string[]>(def.Options) ?? Array.Empty<string>();
                if (!options.Contains(value))
                    return $"扩展字段「{def.Name}」的值必须是候选值之一";
            }
        }
        return null;
    }

    private static bool IsValidIgnoreRegions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                foreach (var key in new[] { "x", "y", "w", "h" })
                    if (!item.TryGetProperty(key, out var v) ||
                        v.ValueKind != System.Text.Json.JsonValueKind.Number)
                        return false;
            }
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

}
