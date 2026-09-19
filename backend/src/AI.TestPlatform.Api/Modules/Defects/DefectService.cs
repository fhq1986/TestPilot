using System.Net;
using System.Text;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Common;
using AI.TestPlatform.Application.Common;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Modules.Defects;

/// <summary>
/// 缺陷业务逻辑。统计口径集中在这里，避免「列表页一个算法、仪表盘一个算法」：
/// - 未闭环 = New / Assigned / Fixed（Fixed 未验证仍算欠债）；
/// - 闭环时点取 VerifiedAt（进入 Verified 即视为闭环，Closed 只是验收收尾动作）。
///
/// 「描述」是用户提交的富文本：写侧净化、读侧同样过 <see cref="RichText.Normalize"/>。
/// 读侧那一遍是给**改版前的历史数据**兜底——那批纯文本从未被净化过，
/// 现在要进 v-html 渲染，不补一道净化就等于把老数据变成了存储型 XSS。
/// </summary>
public class DefectService
{
    /// <summary>关键字搜索时按标题匹配，沿用项目名搜索的 Contains 语义</summary>
    private const int MaxPageSize = 100;

    private readonly TestDbContext _db;

    public DefectService(TestDbContext db) => _db = db;

    // ------------------------------ 查询

    public async Task<PagedResult<DefectListItemDto>> ListAsync(
        Guid? projectId, DefectStatus? status, DefectSeverity? severity,
        Guid? assignedToId, Guid? executionId, Guid? testCaseId, string? search,
        int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 ? 20 : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        var query = _db.Defects.AsNoTracking();
        if (projectId.HasValue) query = query.Where(d => d.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(d => d.Status == status.Value);
        if (severity.HasValue) query = query.Where(d => d.Severity == severity.Value);
        if (assignedToId.HasValue) query = query.Where(d => d.AssignedToId == assignedToId.Value);
        if (executionId.HasValue) query = query.Where(d => d.FoundInExecutionId == executionId.Value);
        if (testCaseId.HasValue)
            query = query.Where(d => d.FoundInTestCaseId == testCaseId.Value ||
                                     _db.DefectCases.Any(dc => dc.DefectId == d.Id && dc.TestCaseId == testCaseId.Value));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(d => d.Title.Contains(keyword));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => new DefectListItemDto(
                d.Id, d.ProjectId, d.Project.Name, d.Title,
                d.Severity, d.Status,
                d.AssignedTo != null ? (d.AssignedTo.DisplayName != null && d.AssignedTo.DisplayName != "" ? d.AssignedTo.DisplayName : d.AssignedTo.Username) : null,
                d.CreatedBy != null ? (d.CreatedBy.DisplayName != null && d.CreatedBy.DisplayName != "" ? d.CreatedBy.DisplayName : d.CreatedBy.Username) : null,
                d.FoundInExecutionId, d.FoundInStepOrder,
                d.FoundInTestCase != null ? d.FoundInTestCase.Name : null,
                d.ExternalRef,
                d.CreatedAt, d.FixedAt, d.VerifiedAt))
            .ToListAsync(ct);

        return new PagedResult<DefectListItemDto>(items, total, page, pageSize);
    }

    public async Task<DefectDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var defect = await _db.Defects.AsNoTracking()
            .Include(d => d.Project)
            .Include(d => d.AssignedTo)
            .Include(d => d.CreatedBy)
            .Include(d => d.VerifiedBy)
            .Include(d => d.FoundInExecution)
            .Include(d => d.FoundInTestCase)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (defect is null) return null;

        var cases = await _db.DefectCases.AsNoTracking()
            .Where(dc => dc.DefectId == id)
            .Select(dc => new DefectCaseLinkDto(dc.TestCaseId, dc.TestCase.Name, dc.TestCase.Module))
            .ToListAsync(ct);

        var occurrences = await _db.DefectOccurrences.AsNoTracking()
            .Where(o => o.DefectId == id)
            .OrderByDescending(o => o.OccurredAt)
            .Select(o => new DefectOccurrenceDto(o.Id, o.ExecutionId, o.StepOrder, o.OccurredAt))
            .ToListAsync(ct);

        static string? DisplayName(User? u) =>
            u == null ? null : string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName;

        return new DefectDetailDto(
            defect.Id, defect.ProjectId, defect.Project.Name, defect.Title,
            RichText.Normalize(defect.Description),
            defect.Severity, defect.Status,
            defect.AssignedToId, DisplayName(defect.AssignedTo),
            defect.CreatedById, DisplayName(defect.CreatedBy),
            defect.FoundInExecutionId, defect.FoundInStepOrder, defect.FoundInTestCaseId,
            defect.FoundInTestCase?.Name,
            defect.ExternalRef, defect.ResolutionNote,
            defect.CreatedAt, defect.UpdatedAt, defect.FixedAt, defect.VerifiedAt,
            DisplayName(defect.VerifiedBy),
            cases, occurrences);
    }

    // ------------------------------ 创建（含一键转缺陷的证据快照）

    public async Task<DefectDetailDto> CreateAsync(CreateDefectRequest request, Guid? currentUserId, CancellationToken ct)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
            throw new InvalidOperationException("缺陷标题不能为空");
        if (title.Length > 200)
            throw new InvalidOperationException("缺陷标题不能超过 200 个字符");

        var projectExists = await _db.Projects.AnyAsync(p => p.Id == request.ProjectId, ct);
        if (!projectExists)
            throw new InvalidOperationException("项目不存在");
        if (request.AssignedToId.HasValue &&
            !await _db.Users.AnyAsync(u => u.Id == request.AssignedToId.Value, ct))
            throw new InvalidOperationException("指派的负责人不存在");

        // 关联用例先校验。ResolveCaseIdsAsync 只查库不写库，所以可以放在 Add 主行之前
        var linkedCaseIds = await ResolveCaseIdsAsync(request.ProjectId, request.TestCaseIds, ct);

        var defect = new Defect
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Title = title,
            Description = RichText.Normalize(request.Description),
            Severity = request.Severity,
            Status = DefectStatus.New,
            AssignedToId = request.AssignedToId,
            CreatedById = currentUserId,
            ExternalRef = request.ExternalRef?.Trim(),
        };

        // 一键转缺陷：快照「首次发现」信息 + 自动预填证据 + 关联用例
        if (request.FoundInExecutionId.HasValue)
        {
            var execution = await _db.Executions.AsNoTracking()
                .Include(e => e.TestCase)
                .Include(e => e.Results)
                .FirstOrDefaultAsync(e => e.Id == request.FoundInExecutionId.Value, ct);
            if (execution is null)
                throw new InvalidOperationException("来源执行记录不存在");

            defect.FoundInExecutionId = execution.Id;
            defect.FoundInStepOrder = request.FoundInStepOrder;
            defect.FoundInTestCaseId = execution.TestCaseId;

            var result = request.FoundInStepOrder is null
                ? execution.Results
                    .Where(r => r.ErrorMessage != null)
                    .OrderBy(r => r.StepOrder)
                    .FirstOrDefault()
                : execution.Results.FirstOrDefault(r => r.StepOrder == request.FoundInStepOrder.Value);

            var evidence = new List<string>();
            if (result?.ErrorMessage is not null)
                evidence.Add($"【错误信息】{result.ErrorMessage}");
            if (execution.AIDiagnosis is not null)
                evidence.Add($"【AI 诊断】{execution.AIDiagnosis}");
            if (result?.ScreenshotUrl is not null)
                evidence.Add($"【截图】{result.ScreenshotUrl}");
            if (execution.TraceUrl is not null)
                evidence.Add($"【执行回放】{execution.TraceUrl}");
            if (result?.Log is not null)
                evidence.Add($"【日志】{result.Log}");

            if (evidence.Count > 0)
            {
                var headerLines = new List<string>
                {
                    $"来源：用例「{execution.TestCase?.Name ?? "(已删除)"}」",
                };
                if (request.FoundInStepOrder is not null)
                    headerLines.Add($"第 {request.FoundInStepOrder + 1} 步");
                headerLines.Add($"执行时间：{execution.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                if (execution.EnvironmentSnapshot != null)
                    headerLines.Add($"环境：{execution.EnvironmentSnapshot.Name}");

                // 描述现在是富文本（走 v-html 渲染），证据块就必须拼成 HTML。
                // **每一段都要 HtmlEncode**：错误信息 / 日志是执行产出的任意文本，
                // 直接拼进 HTML 等于把执行日志变成了注入点——一份被污染的执行日志
                // 可以在查看缺陷的人浏览器里执行脚本，而且完全看不出是哪儿来的。
                var block = new StringBuilder();
                block.Append("<p>")
                     .Append(string.Join("<br>", headerLines.Select(WebUtility.HtmlEncode)))
                     .Append("</p>");
                foreach (var item in evidence)
                    block.Append("<p>").Append(WebUtility.HtmlEncode(item)).Append("</p>");

                defect.Description = string.IsNullOrWhiteSpace(defect.Description)
                    ? block.ToString()
                    : $"{defect.Description}<hr>{block}";
            }

            // 「来源用例」也并进关联集合：它和表单里勾选的本来就是同一张表（DefectCases），
            // 合并去重后一次写入，免得"来源用例"和"关联用例"变成两套说法却指向不同数据
            if (execution.TestCaseId.HasValue)
                linkedCaseIds.Add(execution.TestCaseId.Value);
        }

        _db.Defects.Add(defect);

        foreach (var caseId in linkedCaseIds.Distinct())
            _db.DefectCases.Add(new DefectCase { DefectId = defect.Id, TestCaseId = caseId });

        await _db.SaveChangesAsync(ct);
        return (await GetAsync(defect.Id, ct))!;
    }

    // ------------------------------ 编辑 / 流转

    public async Task<DefectDetailDto?> UpdateAsync(Guid id, UpdateDefectRequest request, CancellationToken ct)
    {
        var defect = await _db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (defect is null) return null;

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
            throw new InvalidOperationException("缺陷标题不能为空");
        if (title.Length > 200)
            throw new InvalidOperationException("缺陷标题不能超过 200 个字符");
        if (request.AssignedToId.HasValue &&
            !await _db.Users.AnyAsync(u => u.Id == request.AssignedToId.Value, ct))
            throw new InvalidOperationException("指派的负责人不存在");

        defect.Title = title;
        defect.Description = RichText.Normalize(request.Description);
        defect.Severity = request.Severity;
        defect.AssignedToId = request.AssignedToId;
        defect.ExternalRef = request.ExternalRef?.Trim();
        defect.UpdatedAt = DateTime.UtcNow;

        // 关联用例按「目标全集」做差集（null = 本次不改动关联）。
        // **刻意不写成"全删再全插"**：那样关联行会短暂消失，而 DefectCases 上还挂着
        // 自动验证（用例回归通过 → Fixed 缺陷自动闭环），中间态可能让它漏判；
        // 顺带也避免把没变的那部分关联无谓地重写一遍。
        if (request.TestCaseIds is not null)
        {
            var desired = await ResolveCaseIdsAsync(defect.ProjectId, request.TestCaseIds, ct);
            var current = await _db.DefectCases
                .Where(dc => dc.DefectId == id)
                .ToListAsync(ct);
            var currentIds = current.Select(dc => dc.TestCaseId).ToHashSet();

            _db.DefectCases.RemoveRange(current.Where(dc => !desired.Contains(dc.TestCaseId)));
            foreach (var caseId in desired.Where(c => !currentIds.Contains(c)))
                _db.DefectCases.Add(new DefectCase { DefectId = id, TestCaseId = caseId });
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// 删除缺陷。
    ///
    /// **子表交给数据库级联**：DefectCase / DefectOccurrence 对 Defect 的外键都是
    /// OnDelete(Cascade)（见 TestDbContext），所以这里删主行即可。
    /// 不要改成"先逐张删子表再删主表"——那种写法漏一张就是 23503 外键错误，
    /// 而且级联规则本来就已经在数据库里写死了一份。
    ///
    /// 删除是**物理删除**，语义是"这条是误建 / 重复 / 填错了"。
    /// "不想再看到它"那种诉求由状态流转覆盖（驳回 / 挂起 / 关闭），
    /// 所以删除不需要再挂一层软删除字段；留档靠端点上的审计日志。
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var defect = await _db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (defect is null) return false;

        _db.Defects.Remove(defect);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>批量删除：关联用例 / 复现流水由数据库级联清理，返回删除数与被跳过项。</summary>
    public async Task<BatchDeleteResultDto> DeleteManyAsync(List<Guid> ids, CancellationToken ct)
    {
        var distinctIds = ids.Distinct().ToList();
        var defects = await _db.Defects.Where(d => distinctIds.Contains(d.Id)).ToListAsync(ct);
        _db.Defects.RemoveRange(defects);

        var found = defects.Select(d => d.Id).ToHashSet();
        var skipped = distinctIds.Where(id => !found.Contains(id))
            .Select(id => new BatchDeleteSkippedItem(id, null, "缺陷不存在"))
            .ToList();

        await _db.SaveChangesAsync(ct);
        return new BatchDeleteResultDto(defects.Count, skipped);
    }

    /// <summary>
    /// 状态流转。验证权限在端点层已收敛到 ManageTestCases——提交人本身是工程师/管理员，
    /// 「测试负责人」同样持有该权限，满足「提交人或测试负责人都可验证」的规则。
    /// </summary>
    public async Task<DefectDetailDto?> TransitionAsync(
        Guid id, DefectTransitionRequest request, Guid? currentUserId, CancellationToken ct)
    {
        var defect = await _db.Defects.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (defect is null) return null;

        var note = request.Note?.Trim();
        var now = DateTime.UtcNow;
        switch (request.Action?.ToLowerInvariant())
        {
            case "assign":
                if (!request.AssignedToId.HasValue)
                    throw new InvalidOperationException("指派时必须指定负责人");
                defect.AssignedToId = request.AssignedToId.Value;
                if (defect.Status is DefectStatus.New or DefectStatus.Rejected or DefectStatus.Deferred)
                    defect.Status = DefectStatus.Assigned;
                break;

            case "fix":
                defect.Status = DefectStatus.Fixed;
                defect.FixedAt = now;
                defect.ResolutionNote = note;
                break;

            case "verify":
                if (defect.Status is not (DefectStatus.Fixed or DefectStatus.Verified))
                    throw new InvalidOperationException("只有「已修复」的缺陷可以验证通过");
                defect.Status = DefectStatus.Verified;
                defect.VerifiedAt = now;
                defect.VerifiedById = currentUserId;
                break;

            case "close":
                if (defect.Status is not (DefectStatus.Verified or DefectStatus.Fixed))
                    throw new InvalidOperationException("只有验证通过（或已修复）的缺陷可以关闭");
                defect.Status = DefectStatus.Closed;
                break;

            case "reject":
            case "defer":
                if (string.IsNullOrWhiteSpace(note))
                    throw new InvalidOperationException(request.Action == "reject" ? "驳回必须填写原因" : "挂起必须填写原因");
                defect.Status = request.Action!.ToLowerInvariant() == "reject"
                    ? DefectStatus.Rejected
                    : DefectStatus.Deferred;
                defect.ResolutionNote = note;
                break;

            case "reopen":
                if (defect.Status is not (DefectStatus.Rejected or DefectStatus.Deferred or DefectStatus.Closed))
                    throw new InvalidOperationException("只有被驳回 / 挂起 / 关闭的缺陷可以重新打开");
                defect.Status = DefectStatus.New;
                defect.FixedAt = null;
                defect.VerifiedAt = null;
                defect.VerifiedById = null;
                break;

            default:
                throw new InvalidOperationException($"未知的流转动作: {request.Action}");
        }

        defect.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ------------------------------ 用例关联 / 复现流水

    public async Task<bool> LinkCaseAsync(Guid defectId, Guid testCaseId, CancellationToken ct)
    {
        var exists = await _db.Defects.AnyAsync(d => d.Id == defectId, ct);
        if (!exists) return false;
        var caseExists = await _db.TestCases.AnyAsync(t => t.Id == testCaseId, ct);
        if (!caseExists) throw new InvalidOperationException("用例不存在");

        var already = await _db.DefectCases
            .AnyAsync(dc => dc.DefectId == defectId && dc.TestCaseId == testCaseId, ct);
        if (!already)
        {
            _db.DefectCases.Add(new DefectCase { DefectId = defectId, TestCaseId = testCaseId });
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> UnlinkCaseAsync(Guid defectId, Guid testCaseId, CancellationToken ct)
    {
        var link = await _db.DefectCases
            .FirstOrDefaultAsync(dc => dc.DefectId == defectId && dc.TestCaseId == testCaseId, ct);
        if (link is null) return false;
        _db.DefectCases.Remove(link);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// 校验一组「关联用例」id 并返回去重后的结果。新增/编辑缺陷表单都用它。
    ///
    /// **必须与缺陷同项目**：前端下拉本来就是按项目过滤的，这里再校验一遍是因为
    /// 接口是公开可调的——前端过滤拦不住直接调 API 的人，串了项目会让别的项目的
    /// 缺陷里冒出一条对不上的关联。
    ///
    /// 被软删除的用例查不到（`TestCases` 有全局过滤器），所以和"不存在"归为同一句报错：
    /// 关联一条已删除的用例之后它永远不会再被跑到，这条关联是死链。
    /// </summary>
    private async Task<List<Guid>> ResolveCaseIdsAsync(
        Guid projectId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return new List<Guid>();

        var distinct = ids.Distinct().ToList();
        var matched = await _db.TestCases.AsNoTracking()
            .Where(t => distinct.Contains(t.Id) && t.ProjectId == projectId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (matched.Count != distinct.Count)
            throw new InvalidOperationException("关联用例不存在、已删除，或不属于缺陷所在项目");

        return matched;
    }

    /// <summary>认领：把某次执行的失败步骤记到该缺陷名下（去重）。执行不存在时抛业务异常。</summary>
    public async Task<bool> AddOccurrenceAsync(Guid defectId, Guid executionId, int stepOrder, CancellationToken ct)
    {
        if (!await _db.Defects.AnyAsync(d => d.Id == defectId, ct)) return false;
        if (!await _db.Executions.AnyAsync(e => e.Id == executionId, ct))
            throw new InvalidOperationException("执行记录不存在");

        var exists = await _db.DefectOccurrences.AnyAsync(o =>
            o.DefectId == defectId && o.ExecutionId == executionId && o.StepOrder == stepOrder, ct);
        if (!exists)
        {
            _db.DefectOccurrences.Add(new DefectOccurrence
            {
                Id = Guid.NewGuid(),
                DefectId = defectId,
                ExecutionId = executionId,
                StepOrder = stepOrder,
            });
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    // ------------------------------ 外部缺陷系统推送

    /// <summary>
    /// 推送缺陷到外部系统（Jira / 禅道，v1 单向创建）。
    /// 重复推送拒绝（ExternalRef 已有 = 已推过）；外部成功后本地记录 ExternalRef。
    /// 若本地写库失败但外部已创建，Error 会带上外部单号让用户手工补登，避免重复开单。
    /// </summary>
    public async Task<ExternalPushResult> PushToExternalAsync(
        Guid defectId, string provider, ExternalDefectPusher pusher, CancellationToken ct)
    {
        var defect = await _db.Defects.FirstOrDefaultAsync(d => d.Id == defectId, ct);
        if (defect is null)
            return new ExternalPushResult(false, "", "", "缺陷不存在");

        if (!string.IsNullOrWhiteSpace(defect.ExternalRef))
            return new ExternalPushResult(false, "", "",
                $"该缺陷已推送到外部系统（{defect.ExternalRef}），请勿重复推送");

        var result = await pusher.PushAsync(provider, defect, ct);
        if (!result.Ok)
            return result;

        try
        {
            defect.ExternalRef = result.ExternalRef;
            defect.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            return new ExternalPushResult(false, result.ExternalRef, result.ExternalUrl,
                $"外部系统已创建成功（{result.ExternalRef}），但本地记录失败：{ex.Message}。请手工补登该单号。");
        }

        return result;
    }

    // ------------------------------ 回归自动闭环

    /// <summary>
    /// 回归通过自动验证（P2）：某用例整条执行通过时，把它关联的、处于 Fixed 状态的缺陷
    /// 自动置为 Verified（带验证人=系统与说明）。重新打开的由人决策，这里不碰。
    /// 返回自动闭环的缺陷数，供 worker 记日志。
    /// </summary>
    public async Task<int> AutoVerifyOnRegressionPassAsync(Guid testCaseId, Guid executionId, CancellationToken ct)
    {
        var toVerify = await _db.Defects
            .Where(d => d.Status == DefectStatus.Fixed &&
                        _db.DefectCases.Any(dc => dc.DefectId == d.Id && dc.TestCaseId == testCaseId))
            .ToListAsync(ct);
        if (toVerify.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var defect in toVerify)
        {
            defect.Status = DefectStatus.Verified;
            defect.VerifiedAt = now;
            defect.ResolutionNote = $"回归通过自动验证（执行 {executionId:N}）";
            defect.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
        return toVerify.Count;
    }

    // ------------------------------ 统计
    public async Task<DefectStatsDto> StatsAsync(Guid? projectId, CancellationToken ct)
    {
        // 说明：这里整体载入内存做聚合，与报告查询的全量载入（审查报告 P1）不同量级——
        // 缺陷是窄行（无 JSONB/大文本列参与聚合），项目级数量在百~千级；
        // 且趋势需要 14 天双序列分组，SQL 侧展开反而不清晰。数量到万级再下推。
        var query = _db.Defects.AsNoTracking();
        if (projectId.HasValue) query = query.Where(d => d.ProjectId == projectId.Value);

        var defects = await query
            .Select(d => new
            {
                d.Severity, d.Status, d.CreatedAt, d.FixedAt, d.VerifiedAt,
            })
            .ToListAsync(ct);

        var open = defects.Where(d => d.Status is DefectStatus.New or DefectStatus.Assigned or DefectStatus.Fixed)
            .ToList();
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);

        // 修复/验证时长：只统计已完成对应动作的缺陷
        var fixDurations = defects.Where(d => d.FixedAt.HasValue)
            .Select(d => (d.FixedAt!.Value - d.CreatedAt).TotalHours).ToList();
        var verifyDurations = defects.Where(d => d.VerifiedAt.HasValue && d.FixedAt.HasValue)
            .Select(d => (d.VerifiedAt!.Value - d.FixedAt!.Value).TotalHours).ToList();

        // 近 14 天趋势：按自然日（UTC）聚合新增与闭环
        var trendStart = now.Date.AddDays(-13);
        var trend = Enumerable.Range(0, 14).Select(i =>
        {
            var day = trendStart.AddDays(i);
            return new DefectTrendPoint(
                day.ToString("MM-dd"),
                defects.Count(d => d.CreatedAt >= day && d.CreatedAt < day.AddDays(1)),
                defects.Count(d => d.VerifiedAt.HasValue &&
                                   d.VerifiedAt.Value >= day && d.VerifiedAt.Value < day.AddDays(1)));
        }).ToList();

        return new DefectStatsDto(
            open.Count,
            open.Count(d => d.Severity == DefectSeverity.Critical),
            open.Count(d => d.Severity == DefectSeverity.Major),
            open.Count(d => d.Severity == DefectSeverity.Normal),
            open.Count(d => d.Severity == DefectSeverity.Suggestion),
            defects.Count(d => d.CreatedAt >= weekAgo),
            defects.Count(d => d.VerifiedAt.HasValue && d.VerifiedAt.Value >= weekAgo),
            fixDurations.Count > 0 ? Math.Round(fixDurations.Average(), 1) : null,
            verifyDurations.Count > 0 ? Math.Round(verifyDurations.Average(), 1) : null,
            trend);
    }
}
