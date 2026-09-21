using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Application.TestPlans;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.TestPlans;

/// <summary>
/// 测试计划报告导出（xlsx）。
///
/// 与执行报告的区别：计划的交付物是**验收证据**，所以首屏是达标判定与依据，
/// 而不是逐条用例明细。四张 sheet：达标判定 / 轮次趋势 / 模块通过率 / 阻断用例。
/// 样式沿用 <see cref="ExcelStyling"/>，与平台其它导出保持一套观感。
/// </summary>
public class TestPlanReportService
{
    /// <summary>模块维度的原始执行行（EF 匿名类型不能跨方法传递，落成显式记录更稳）</summary>
    private record ModuleRow(string Module, ExecutionStatus Status, bool IsFlaky);

    private readonly TestDbContext _db;
    private readonly TestPlanService _plans;

    public TestPlanReportService(TestDbContext db, TestPlanService plans)
    {
        _db = db;
        _plans = plans;
    }

    public async Task<(byte[] Content, string FileName)?> GenerateAsync(Guid planId, CancellationToken ct)
    {
        var plan = await _db.TestPlans.AsNoTracking().Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null) return null;

        var rounds = await _plans.LoadRoundsRawAsync(planId, ct);

        // 缺陷验收门槛（P2）：与 BuildGateAsync 同一份数据源，报告与页面口径一致
        var openCritical = 0;
        if (plan.DefectGateEnabled)
        {
            openCritical = await _db.Defects.AsNoTracking()
                .CountAsync(d => d.ProjectId == plan.ProjectId
                                 && (d.Severity == DefectSeverity.Critical || d.Severity == DefectSeverity.Major)
                                 && (d.Status == DefectStatus.New || d.Status == DefectStatus.Assigned || d.Status == DefectStatus.Fixed), ct);
        }

        // Agent 自愈通过是否计入达标（项目级，默认不计入）——报告与页面口径必须一致
        var treatAgentHealed = await _plans.GetTreatAgentHealedAsPassAsync(plan.ProjectId, ct);
        var gate = PlanGateEvaluator.Evaluate(plan.Name, plan.ReleaseName,
            plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure, plan.GateMode, rounds,
            plan.DefectGateEnabled, openCritical, treatAgentHealed);

        // 计划范围内的缺陷：与计划范围用例存在关联（DefectCase 或首发现用例）的缺陷
        var planCaseIds = await _db.TestPlanItems.AsNoTracking()
            .Where(i => i.PlanId == planId)
            .Select(i => i.TestCaseId)
            .ToListAsync(ct);
        var defects = planCaseIds.Count == 0
            ? new List<Defect>()
            : await _db.Defects.AsNoTracking()
                .Include(d => d.AssignedTo)
                .Where(d => _db.DefectCases.Any(dc => dc.DefectId == d.Id && planCaseIds.Contains(dc.TestCaseId))
                            || (d.FoundInTestCaseId != null && planCaseIds.Contains(d.FoundInTestCaseId.Value)))
                .OrderBy(d => d.Status)
                .ThenByDescending(d => d.Severity)
                .ThenByDescending(d => d.CreatedAt)
                .ToListAsync(ct);

        var moduleRows = (await _db.Executions.AsNoTracking()
                .Where(e => e.PlanId == planId)
                .Select(e => new
                {
                    Module = e.TestCase != null && e.TestCase.Module != null ? e.TestCase.Module : "未分类",
                    e.Status,
                    IsFlaky = e.TestCase != null && e.TestCase.IsFlaky,
                })
                .ToListAsync(ct))
            .Select(r => new ModuleRow(r.Module!, r.Status, r.IsFlaky))
            .ToList();

        using var workbook = new XLWorkbook();

        BuildGateSheet(workbook, plan, gate, openCritical);
        BuildTrendSheet(workbook, rounds, plan);
        BuildModuleSheet(workbook, moduleRows, plan);
        BuildBlockingSheet(workbook, gate.BlockingCases);
        BuildDefectListSheet(workbook, defects);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var safeName = Sanitize(plan.Name);
        return (stream.ToArray(), $"{safeName}-验收报告-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>达标判定：验收时最需要一页就能说清「过没过、为什么」</summary>
    private static void BuildGateSheet(XLWorkbook workbook, TestPlan plan, PlanGateResult gate, int openCritical)
    {
        var ws = workbook.Worksheets.Add("达标判定");
        ws.Cell(1, 1).Value = gate.Passed ? "✅ 已达标" : "❌ 未达标";
        ws.Range(1, 1, 1, 4).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16)
            .Font.SetFontColor(gate.Passed ? ExcelStyling.Success : ExcelStyling.Danger);
        ws.Row(1).Height = 26;

        var rows = new (string Label, string Value)[]
        {
            ("计划名称", plan.Name),
            ("版本标识", plan.ReleaseName ?? "—"),
            ("负责人", plan.Owner is null ? "—" : plan.Owner.DisplayName ?? plan.Owner.Username),
            ("计划周期", $"{(plan.StartsAt.HasValue ? plan.StartsAt.Value.ToString("yyyy-MM-dd") : "未设置")} ~ " +
                     $"{(plan.EndsAt.HasValue ? plan.EndsAt.Value.ToString("yyyy-MM-dd") : "未设置")}"),
            ("目标通过率", plan.TargetPassRate.ToString("P0")),
            ("达标口径", plan.GateMode == PlanGateMode.AnyRound ? "任意一轮达标" : "最后一轮达标"),
            ("Error 口径", plan.AllowErrors ? "允许 Error" : "不允许 Error"),
            ("flaky 口径", plan.ExcludeFlakyFromFailure ? "排除不稳定用例的失败样本" : "不排除 flaky"),
            ("缺陷门槛", plan.DefectGateEnabled
                ? $"开启：未闭环致命/严重缺陷须为 0（当前 {openCritical} 个）"
                : "未开启"),
            ("判定依据轮次", gate.EvaluatedRoundNo is null ? "尚无轮次" : $"第 {gate.EvaluatedRoundNo} 轮"),
            ("参与判定样本", gate.Stats.Total.ToString()),
            ("通过", gate.Stats.Passed.ToString()),
            ("失败", gate.Stats.Failed.ToString()),
            ("错误", gate.Stats.Error.ToString()),
            ("跳过", gate.Stats.Skipped.ToString()),
            ("实际通过率", gate.Stats.PassRate.ToString("P1")),
        };

        var r = 3;
        foreach (var (label, value) in rows)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.SetBold();
            ws.Cell(r, 1).Style.Fill.SetBackgroundColor(ExcelStyling.SummaryBg);
            ws.Cell(r, 2).Value = value;
            ws.Range(r, 1, r, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(r, 1, r, 2).Style.Border.OutsideBorderColor = ExcelStyling.Border;
            r++;
        }

        r++;
        ws.Cell(r, 1).Value = "判定依据";
        ws.Cell(r, 1).Style.Font.SetBold().Font.SetFontColor(ExcelStyling.Primary);
        r++;
        if (gate.Reasons.Count == 0)
        {
            ws.Cell(r, 1).Value = "全部条件满足，无未达标项";
            ws.Cell(r, 1).Style.Font.SetFontColor(ExcelStyling.Success);
        }
        else
        {
            foreach (var reason in gate.Reasons)
            {
                ws.Cell(r, 1).Value = "· " + reason;
                if (reason.Contains("低于目标") || reason.Contains("未允许") || reason.Contains("缺陷验收门槛"))
                    ws.Cell(r, 1).Style.Font.SetFontColor(ExcelStyling.Danger);
                r++;
            }
        }

        ws.Column(1).Width = 18;
        ws.Column(2).Width = 56;
        ExcelStyling.NormalizeFont(ws);
    }

    private static void BuildTrendSheet(XLWorkbook workbook, List<PlanRoundRaw> rounds, TestPlan plan)
    {
        var ws = workbook.Worksheets.Add("轮次趋势");
        var headers = new[] { "轮次", "开始时间", "完成时间", "参与判定", "通过", "失败", "错误", "跳过", "通过率", "是否达标" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(1, 1, 1, headers.Length));

        var r = 1;
        foreach (var round in rounds.OrderBy(x => x.RoundNo))
        {
            r++;
            var (passed, passRate, failed, error, denominator, _, _) = PlanGateEvaluator.Judge(
                round, plan.TargetPassRate, plan.AllowErrors, plan.ExcludeFlakyFromFailure);
            ws.Cell(r, 1).Value = $"第 {round.RoundNo} 轮";
            ws.Cell(r, 2).Value = round.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r, 3).Value = round.CompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "进行中";
            ws.Cell(r, 4).Value = denominator;
            ws.Cell(r, 5).Value = passed;
            ws.Cell(r, 6).Value = failed;
            ws.Cell(r, 7).Value = error;
            ws.Cell(r, 8).Value = round.Skipped;
            ws.Cell(r, 9).Value = passRate.ToString("P1");
            ws.Cell(r, 10).Value = passed ? "达标" : "未达标";
            ws.Cell(r, 10).Style.Font.SetFontColor(passed ? ExcelStyling.Success : ExcelStyling.Danger);
            ExcelStyling.ApplyRowStyle(ws.Range(r, 1, r, headers.Length), r % 2 == 0);
        }

        var widths = new[] { 12, 18, 18, 10, 8, 8, 8, 8, 10, 10 };
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.FreezeRows(1);
        ExcelStyling.NormalizeFont(ws);
    }

    private static void BuildModuleSheet(XLWorkbook workbook,
        List<ModuleRow> moduleRows, TestPlan plan)
    {
        var ws = workbook.Worksheets.Add("模块通过率");
        var headers = new[] { "模块", "参与判定", "通过", "失败", "错误", "跳过", "通过率" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(1, 1, 1, headers.Length));

        var byModule = moduleRows
            .GroupBy(m => m.Module)
            .Select(g =>
            {
                var flakyFailures = plan.ExcludeFlakyFromFailure
                    ? g.Count(x => x.IsFlaky && x.Status is ExecutionStatus.Failed or ExecutionStatus.Error)
                    : 0;
                var skipped = g.Count(x => x.Status == ExecutionStatus.Skipped);
                var passed = g.Count(x => x.Status == ExecutionStatus.Passed);
                var failed = g.Count(x => x.Status == ExecutionStatus.Failed)
                             - (plan.ExcludeFlakyFromFailure
                                 ? g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Failed)
                                 : 0);
                var error = g.Count(x => x.Status == ExecutionStatus.Error)
                            - (plan.ExcludeFlakyFromFailure
                                ? g.Count(x => x.IsFlaky && x.Status == ExecutionStatus.Error)
                                : 0);
                var denominator = Math.Max(0, g.Count() - skipped - flakyFailures);
                return new
                {
                    Module = g.Key, Denominator = denominator, Passed = passed,
                    Failed = failed, Error = error, Skipped = skipped,
                    Rate = denominator > 0 ? (double)passed / denominator : 0,
                };
            })
            .OrderByDescending(m => m.Failed + m.Error)
            .ThenBy(m => m.Module)
            .ToList();

        var r = 1;
        foreach (var m in byModule)
        {
            r++;
            ws.Cell(r, 1).Value = m.Module;
            ws.Cell(r, 2).Value = m.Denominator;
            ws.Cell(r, 3).Value = m.Passed;
            ws.Cell(r, 4).Value = m.Failed;
            ws.Cell(r, 5).Value = m.Error;
            ws.Cell(r, 6).Value = m.Skipped;
            ws.Cell(r, 7).Value = m.Rate.ToString("P1");
            ws.Cell(r, 7).Style.Font.SetFontColor(
                m.Rate >= plan.TargetPassRate ? ExcelStyling.Success : ExcelStyling.Danger);
            ExcelStyling.ApplyRowStyle(ws.Range(r, 1, r, headers.Length), r % 2 == 0);
        }

        var widths = new[] { 24, 10, 8, 8, 8, 8, 10 };
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.SheetView.FreezeRows(1);
        ExcelStyling.NormalizeFont(ws);
    }

    private static void BuildBlockingSheet(XLWorkbook workbook, IReadOnlyList<PlanBlockingCaseDto> blocking)
    {
        var ws = workbook.Worksheets.Add("阻断用例");
        var headers = new[] { "序号", "用例名称", "模块", "状态", "不稳定", "错误信息" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(1, 1, 1, headers.Length));

        if (blocking.Count == 0)
        {
            ws.Cell(2, 1).Value = "无阻断用例";
            ws.Cell(2, 1).Style.Font.SetFontColor(ExcelStyling.Success);
        }

        var r = 1;
        foreach (var (item, index) in blocking.Select((b, i) => (b, i)))
        {
            r++;
            ws.Cell(r, 1).Value = index + 1;
            ws.Cell(r, 2).Value = item.Name;
            ws.Cell(r, 3).Value = item.Module ?? "—";
            ws.Cell(r, 4).Value = item.Status == ExecutionStatus.Error ? "错误" : "失败";
            ws.Cell(r, 4).Style.Font.SetFontColor(ExcelStyling.Danger);
            ws.Cell(r, 5).Value = item.IsFlaky ? "是" : "否";
            ws.Cell(r, 6).Value = item.ErrorMessage ?? "—";
            ws.Cell(r, 6).Style.Alignment.WrapText = true;
            ExcelStyling.ApplyRowStyle(ws.Range(r, 1, r, headers.Length), r % 2 == 0);
        }

        ws.Column(1).Width = 6;
        ws.Column(2).Width = 34;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 8;
        ws.Column(5).Width = 10;
        ws.Column(6).Width = 60;
        ws.SheetView.FreezeRows(1);
        ExcelStyling.NormalizeFont(ws);
    }

    /// <summary>
    /// 关联缺陷 sheet（P2）：列出与计划范围用例相关联的缺陷。
    /// 数据与缺陷模块同一套口径；没有关联缺陷时同样成页，写明「无」以免被当成漏导。
    /// </summary>
    private static void BuildDefectListSheet(XLWorkbook workbook, List<Defect> defects)
    {
        var ws = workbook.Worksheets.Add("关联缺陷");
        var openCount = defects.Count(d => d.Status is DefectStatus.New or DefectStatus.Assigned or DefectStatus.Fixed);

        var headers = new[] { "编号", "标题", "严重度", "状态", "负责人", "创建时间", "闭环时间", "流转备注" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(1, 1, 1, headers.Length));

        if (defects.Count == 0)
        {
            ws.Cell(2, 1).Value = "计划范围用例暂无关联缺陷";
            ws.Cell(2, 1).Style.Font.SetFontColor(ExcelStyling.Success);
        }

        var r = 1;
        foreach (var defect in defects)
        {
            r++;
            ws.Cell(r, 1).Value = $"DEF-{defect.Id.ToString("N")[..8].ToUpper()}";
            ws.Cell(r, 2).Value = defect.Title;
            ws.Cell(r, 3).Value = defect.Severity switch
            {
                DefectSeverity.Critical => "致命",
                DefectSeverity.Major => "严重",
                DefectSeverity.Normal => "一般",
                _ => "建议性",
            };
            ws.Cell(r, 4).Value = defect.Status switch
            {
                DefectStatus.New => "新建",
                DefectStatus.Assigned => "已指派",
                DefectStatus.Fixed => "已修复",
                DefectStatus.Verified => "已验证",
                DefectStatus.Closed => "已关闭",
                DefectStatus.Rejected => "已驳回",
                _ => "已挂起",
            };
            ws.Cell(r, 5).Value = defect.AssignedTo is null
                ? "—"
                : string.IsNullOrWhiteSpace(defect.AssignedTo.DisplayName)
                    ? defect.AssignedTo.Username
                    : defect.AssignedTo.DisplayName;
            ws.Cell(r, 6).Value = defect.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r, 7).Value = defect.VerifiedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—";
            ws.Cell(r, 8).Value = defect.ResolutionNote ?? "—";

            var closed = defect.Status is DefectStatus.Verified or DefectStatus.Closed;
            var terminal = defect.Status is DefectStatus.Rejected or DefectStatus.Deferred;
            ExcelStyling.ApplyRowStyle(ws.Range(r, 1, r, headers.Length), r % 2 == 0);
            ws.Cell(r, 4).Style.Font.SetFontColor(
                terminal ? ExcelStyling.Muted : closed ? ExcelStyling.Success : ExcelStyling.Danger);
            ws.Cell(r, 2).Style.Alignment.WrapText = true;
            ws.Cell(r, 8).Style.Alignment.WrapText = true;
        }

        if (defects.Count > 0)
        {
            ws.Cell(r + 2, 1).Value = $"合计 {defects.Count} 项，未闭环 {openCount} 项";
            ws.Cell(r + 2, 1).Style.Font.SetBold().Font.SetFontColor(
                openCount > 0 ? ExcelStyling.Warn : ExcelStyling.Success);
        }

        ws.Column(1).Width = 11;
        ws.Column(2).Width = 46;
        ws.Column(3).Width = 9;
        ws.Column(4).Width = 9;
        ws.Column(5).Width = 10;
        ws.Column(6).Width = 17;
        ws.Column(7).Width = 17;
        ws.Column(8).Width = 30;
        ws.SheetView.FreezeRows(1);
        ExcelStyling.NormalizeFont(ws);
    }

    /// <summary>文件名里剔掉路径分隔符等非法字符（计划名是用户输入）</summary>
    private static string Sanitize(string name)    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "测试计划" : cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
