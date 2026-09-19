using ClosedXML.Excel;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>
/// Excel 统一样式（测试报告与导入模板共用）：
/// 微软雅黑、主色 #1F3B73、表头深蓝底白字、斑马纹、细边框、冻结与打印设置。
/// </summary>
public static class ExcelStyling
{
    public const string FontName = "微软雅黑";

    public static readonly XLColor Primary = XLColor.FromHtml("#1F3B73");
    public static readonly XLColor HeaderBg = XLColor.FromHtml("#1F3B73");
    public static readonly XLColor ZebraBg = XLColor.FromHtml("#F4F8FD");
    public static readonly XLColor Border = XLColor.FromHtml("#C9D8EA");
    public static readonly XLColor SummaryBg = XLColor.FromHtml("#EAF1FB");
    public static readonly XLColor Success = XLColor.FromHtml("#1F7A33");
    public static readonly XLColor Danger = XLColor.FromHtml("#C0272D");
    public static readonly XLColor Warn = XLColor.FromHtml("#B26A00");
    public static readonly XLColor Muted = XLColor.FromHtml("#8A94A6");

    /// <summary>表头：深蓝底 + 白字 + 居中 + 细边框。</summary>
    public static void ApplyHeaderStyle(IXLRange range)
    {
        range.Style.Font.SetBold().Font.SetFontSize(10).Font.SetFontColor(XLColor.White);
        range.Style.Fill.SetBackgroundColor(HeaderBg);
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        ApplyTableBorders(range);
    }

    /// <summary>数据行：斑马纹 + 细边框 + 垂直居中。</summary>
    public static void ApplyRowStyle(IXLRange range, bool zebra)
    {
        ApplyTableBorders(range);
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Font.SetFontSize(10);
        if (zebra)
            range.Style.Fill.SetBackgroundColor(ZebraBg);
    }

    /// <summary>统一细边框（外框 + 内线，避免逐行描边产生双线）。</summary>
    public static void ApplyTableBorders(IXLRange range)
    {
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.OutsideBorderColor = Border;
        range.Style.Border.InsideBorderColor = Border;
    }

    /// <summary>统一全表字体（中文字体，避免默认 Calibri 显示不佳）。</summary>
    public static void NormalizeFont(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        if (used is null)
            return;
        used.Style.Font.FontName = FontName;
    }

    /// <summary>写入标题区：主标题 + 可选副标题（合并居中）。</summary>
    public static void WriteTitle(IXLWorksheet ws, string title, string? subtitle, int lastCol,
        int titleSize = 16)
    {
        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, lastCol).Merge();
        var cell = ws.Cell(1, 1);
        cell.Style.Font.SetBold().Font.SetFontSize(titleSize).Font.SetFontColor(Primary);
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = titleSize >= 16 ? 34 : 28;

        if (string.IsNullOrWhiteSpace(subtitle))
            return;
        ws.Cell(2, 1).Value = subtitle;
        ws.Range(2, 1, 2, lastCol).Merge();
        var sub = ws.Cell(2, 1);
        sub.Style.Font.SetFontSize(9).Font.SetFontColor(Muted);
        sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sub.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(2).Height = 18;
    }

    /// <summary>垂直居中 + 自动换行（用于内容较长的列）。</summary>
    public static void WrapColumns(IXLRange range, bool topAlign = true)
    {
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = topAlign
            ? XLAlignmentVerticalValues.Top
            : XLAlignmentVerticalValues.Center;
    }
}
