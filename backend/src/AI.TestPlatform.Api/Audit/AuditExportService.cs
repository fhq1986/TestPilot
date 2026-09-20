using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.Audit;

/// <summary>
/// 审计日志导出（迭代 D）。
///
/// 合规与安全评审几乎必然要求「把操作记录导出来」，只在页面里翻页看是不够的。
/// 导出遵循**当前筛选条件**而不是全量导出——审计表的行数可以很大，
/// 全量导出既慢又没有使用场景（真要看全量应该走数仓/只读副本）。
/// </summary>
public static class AuditExportService
{
    /// <summary>单次导出的行数上限。超过部分在表头明确说明，而不是静默截断</summary>
    public const int MaxRows = 50_000;

    /// <summary>
    /// 应用筛选条件。列表页与导出共用这一个方法——
    /// 两边各写一遍必然走样，用户会看到「页面筛出来的」和「导出的」不一致。
    /// </summary>
    public static IQueryable<AuditLog> ApplyFilter(IQueryable<AuditLog> query, AuditQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(l => l.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.ResourceType))
            query = query.Where(l => l.ResourceType == filter.ResourceType);
        if (!string.IsNullOrWhiteSpace(filter.Username))
        {
            var keyword = filter.Username.Trim();
            query = query.Where(l => l.Username != null && l.Username.Contains(keyword));
        }
        if (filter.Succeeded is not null)
            query = query.Where(l => l.Succeeded == filter.Succeeded);
        if (filter.From is not null)
            query = query.Where(l => l.CreatedAt >= filter.From);
        if (filter.To is not null)
            query = query.Where(l => l.CreatedAt <= filter.To);
        return query;
    }

    public static async Task<byte[]> BuildAsync(
        TestDbContext db, AuditQuery query, CancellationToken ct)
    {
        var q = ApplyFilter(db.AuditLogs.AsNoTracking(), query);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(l => l.CreatedAt)
            .Take(MaxRows)
            .Select(l => new
            {
                l.CreatedAt, l.Username, l.UserRole, l.Action, l.ResourceType,
                l.ResourceName, l.ResourceId, l.Method, l.Path,
                l.StatusCode, l.Succeeded, l.DurationMs, l.IpAddress, l.Detail, l.ResponseBody,
            })
            .ToListAsync(ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("审计日志");

        const int columns = 15;
        var headers = new[]
        {
            "时间", "操作人", "角色", "动作", "资源类型", "资源名称", "资源ID",
            "方法", "路径", "状态码", "结果", "耗时(ms)", "来源IP", "请求内容", "响应结果",
        };

        // 标题 + 导出条件说明：报告类文件必须先自证「这份数据是什么范围的」
        ws.Cell(1, 1).Value = "审计日志";
        ws.Range(1, 1, 1, columns).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        var filterText = DescribeFilters(query);
        ws.Cell(2, 1).Value = $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}    筛选条件：{filterText}    " +
                              $"命中 {total} 条，已导出 {rows.Count} 条" +
                              (total > rows.Count ? $"（超出单次上限 {MaxRows}，仅保留最新的部分）" : string.Empty);
        ws.Range(2, 1, 2, columns).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Muted);
        ws.Cell(2, 1).Style.Alignment.WrapText = true;
        ws.Row(2).Height = 26;

        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(headerRow, i + 1).Value = headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(headerRow, 1, headerRow, columns));
        ws.Row(headerRow).Height = 20;

        var rowIndex = headerRow;
        foreach (var row in rows)
        {
            rowIndex++;
            ws.Cell(rowIndex, 1).Value = row.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(rowIndex, 2).Value = row.Username ?? "-";
            ws.Cell(rowIndex, 3).Value = RoleLabel(row.UserRole);
            ws.Cell(rowIndex, 4).Value = ActionLabel(row.Action);
            ws.Cell(rowIndex, 5).Value = ResourceLabel(row.ResourceType);
            ws.Cell(rowIndex, 6).Value = row.ResourceName ?? "-";
            ws.Cell(rowIndex, 7).Value = row.ResourceId ?? "-";
            ws.Cell(rowIndex, 8).Value = row.Method;
            ws.Cell(rowIndex, 9).Value = row.Path;
            ws.Cell(rowIndex, 10).Value = row.StatusCode;

            var resultCell = ws.Cell(rowIndex, 11);
            resultCell.Value = row.Succeeded ? "成功" : "失败";
            resultCell.Style.Font.SetFontColor(row.Succeeded ? ExcelStyling.Success : ExcelStyling.Danger);

            ws.Cell(rowIndex, 12).Value = row.DurationMs;
            ws.Cell(rowIndex, 13).Value = row.IpAddress ?? "-";
            ws.Cell(rowIndex, 14).Value = row.Detail ?? string.Empty;
            ws.Cell(rowIndex, 15).Value = row.ResponseBody ?? string.Empty;

            ExcelStyling.ApplyRowStyle(ws.Range(rowIndex, 1, rowIndex, columns), (rowIndex - headerRow) % 2 == 0);
            // 路径列在左侧不换行会撑得很宽，请求/响应内容可能很长，统一靠左并自动换行
            ws.Cell(rowIndex, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            foreach (var col in new[] { 14, 15 })
            {
                ws.Cell(rowIndex, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(rowIndex, col).Style.Alignment.WrapText = true;
            }
        }

        if (rows.Count > 0)
            ws.Range(headerRow, 1, rowIndex, columns).SetAutoFilter();

        ws.SheetView.FreezeRows(headerRow);
        ws.Column(1).Width = 20;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 14;
        ws.Column(6).Width = 22;
        ws.Column(7).Width = 36;
        ws.Column(8).Width = 8;
        ws.Column(9).Width = 42;
        ws.Column(10).Width = 8;
        ws.Column(11).Width = 8;
        ws.Column(12).Width = 10;
        ws.Column(13).Width = 16;
        ws.Column(14).Width = 40;
        ws.Column(15).Width = 40;

        ExcelStyling.NormalizeFont(ws);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string DescribeFilters(AuditQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Action)) parts.Add($"动作={query.Action}");
        if (!string.IsNullOrWhiteSpace(query.ResourceType)) parts.Add($"资源={query.ResourceType}");
        if (!string.IsNullOrWhiteSpace(query.Username)) parts.Add($"操作人包含「{query.Username}」");
        if (query.Succeeded is not null) parts.Add($"结果={(query.Succeeded.Value ? "成功" : "失败")}");
        if (query.From is not null) parts.Add($"起={query.From:yyyy-MM-dd HH:mm}");
        if (query.To is not null) parts.Add($"止={query.To:yyyy-MM-dd HH:mm}");
        return parts.Count == 0 ? "全部记录" : string.Join("，", parts);
    }

    private static string RoleLabel(string? role) => role switch
    {
        nameof(UserRole.SuperAdmin) => "超级管理员",
        nameof(UserRole.Admin) => "管理员",
        nameof(UserRole.Tester) => "测试工程师",
        nameof(UserRole.Viewer) => "只读访客",
        _ => role ?? "-",
    };

    private static string ActionLabel(string action) => action switch
    {
        "Create" => "新建",
        "Update" => "更新",
        "Delete" => "删除",
        "BatchDelete" => "批量删除",
        "Run" => "执行",
        "Stop" => "停止",
        "Import" => "导入",
        "Export" => "导出",
        "ResetPassword" => "重置密码",
        "ChangePassword" => "修改密码",
        "StartRecord" => "开始录制",
        "StopRecord" => "停止录制",
        "SaveRecord" => "保存录制",
        "UpdateSteps" => "更新步骤",
        _ => action,
    };

    private static string ResourceLabel(string type) => type switch
    {
        "Project" => "项目",
        "TestCase" => "测试用例",
        "Execution" => "执行",
        "DataSet" => "数据集",
        "Schedule" => "定时任务",
        "Suite" => "测试套件",
        "Baseline" => "视觉基线",
        "User" => "用户",
        "Setting" => "系统设置",
        "Mock" => "Mock",
        "Environment" => "环境",
        "SharedStep" => "共享步骤",
        "Recorder" => "录制会话",
        _ => type,
    };
}

/// <summary>审计查询条件（列表与导出共用同一组语义，避免两边筛选口径不一致）</summary>
public record AuditQuery(
    string? Action = null, string? ResourceType = null, string? Username = null,
    bool? Succeeded = null, DateTime? From = null, DateTime? To = null);
