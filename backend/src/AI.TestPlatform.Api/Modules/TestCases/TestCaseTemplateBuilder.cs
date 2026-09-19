using ClosedXML.Excel;

namespace AI.TestPlatform.Api.Modules.TestCases;

/// <summary>
/// 生成「测试用例导入模板」xlsx：填写说明表 + 两个示例模块表（功能用例 / 接口用例）。
/// 模板结构必须与 <see cref="ExcelTestCaseParser"/> 保持一致（模块表：首行标题、次行表头、第三行起数据）。
/// </summary>
public static class TestCaseTemplateBuilder
{
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string FileName = "测试用例导入模板.xlsx";

    private static readonly string[] Headers =
        { "用例编号", "测试场景", "操作步骤", "预期结果", "优先级", "类别" };

    private static readonly double[] ColumnWidths = { 16, 24, 44, 44, 9, 10 };

    public static byte[] Build()
    {
        using var workbook = new XLWorkbook();
        BuildGuideSheet(workbook);
        BuildModuleSheet(workbook, "1. 登录认证模块", "1. 登录认证模块 — 用户登录、表单校验",
            new[]
            {
                new[] { "TC-LOGIN-001", "正常登录", "打开登录页，输入正确的用户名和密码，点击登录", "登录成功并跳转到首页", "高", "功能" },
                new[] { "TC-LOGIN-002", "密码为空提交", "用户名填 admin，密码留空，点击登录", "表单提示「请输入密码」，且不发起请求", "中", "功能" },
                new[] { "TC-LOGIN-003", "错误密码登录", "输入正确用户名 + 错误密码，点击登录", "提示「用户名或密码错误」，停留在登录页", "高", "安全" },
            });
        BuildModuleSheet(workbook, "2. 订单管理", "2. 订单管理 — 接口场景示例",
            new[]
            {
                new[] { "TC-ORDER-001", "查询订单详情", "调用 GET /api/orders/{id}，携带有效 Token", "返回 200 与订单详情 JSON", "高", "接口" },
                new[] { "TC-ORDER-002", "订单列表分页", "调用 GET /api/orders?page=1&pageSize=10", "返回当前页数据与总数", "中", "接口" },
            });

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public static string ContentType => XlsxContentType;

    // ---------------------------------------------------------------- 填写说明
    private static void BuildGuideSheet(XLWorkbook workbook)
    {
        const int cols = 4;
        var ws = workbook.Worksheets.Add("填写说明");
        ws.ShowGridLines = false;

        ExcelStyling.WriteTitle(ws, "测试用例导入模板 — 填写说明",
            "按本模板整理用例后，在「测试用例 → 导入用例」中上传即可；示例数据可直接删除或替换", cols, 15);

        var notes = new[]
        {
            "1. 每个功能模块使用一个工作表（如「1. 登录认证模块」）；本说明表与「测试概览」「测试执行记录」「缺陷报告」等非用例表会被自动跳过。",
            "2. 工作表首行可写模块标题，第二行必须是表头，第三行起为用例数据；表头顺序不限，支持同义列名（如「用例名称」「前置条件/操作步骤」「期望结果」）。",
            "3. 优先级填写 高 / 中 / 低（也可直接写 P0 / P1 / P2 / P3），导入时统一映射为标准优先级。",
            "4. 类别可填 功能 / 接口 / 安全 / 性能 等；类别或步骤中含「接口 / API」的用例会被识别为接口类用例。",
            "5. 用例编号在同一模块内唯一；重复编号的用例在导入时按「跳过」或「覆盖更新」处理（导入时可选择）。",
            "6. 导入时可勾选「AI 生成步骤」，由 AI 把「操作步骤 + 预期结果」转换为可执行步骤（Navigate / Fill / Click / AssertText 等）。",
            "7. 「操作步骤」尽量包含具体数据（输入什么、点哪里）；「预期结果」写清页面提示或跳转，便于生成断言。",
        };

        var row = 4;
        foreach (var note in notes)
        {
            ws.Cell(row, 1).Value = note;
            ws.Range(row, 1, row, cols).Merge();
            ws.Cell(row, 1).Style.Font.SetFontSize(10);
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
            ws.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(row).Height = 22;
            row++;
        }

        // 表头字段说明
        row++;
        ws.Cell(row, 1).Value = "列名";
        ws.Cell(row, 2).Value = "是否必填";
        ws.Cell(row, 3).Value = "说明";
        ws.Range(row, 3, row, cols).Merge();
        ExcelStyling.ApplyHeaderStyle(ws.Range(row, 1, row, cols));
        ws.Row(row).Height = 24;

        var fields = new[]
        {
            ("用例编号", "建议填写", "模块内唯一；留空时按「模块序号-行号」自动生成"),
            ("测试场景", "必填", "用例名称，如「密码为空提交」"),
            ("操作步骤", "建议填写", "前置条件与操作步骤，可用换行书写多步"),
            ("预期结果", "建议填写", "期望的页面提示、跳转或接口返回"),
            ("优先级", "选填", "高 / 中 / 低（默认中）"),
            ("类别", "选填", "功能 / 接口 / 安全 / 性能 等"),
        };
        row++;
        var zebra = false;
        foreach (var (name, required, desc) in fields)
        {
            ws.Cell(row, 1).Value = name;
            ws.Cell(row, 2).Value = required;
            ws.Cell(row, 3).Value = desc;
            ws.Range(row, 3, row, cols).Merge();
            var line = ws.Range(row, 1, row, cols);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(row, 2).Style.Font.SetFontColor(required == "必填" ? ExcelStyling.Danger : ExcelStyling.Muted);
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Row(row).Height = 20;
            zebra = !zebra;
            row++;
        }

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 60;
        ws.Column(4).Width = 10;
        ExcelStyling.NormalizeFont(ws);
    }

    // ---------------------------------------------------------------- 示例模块
    private static void BuildModuleSheet(XLWorkbook workbook, string name, string title, string[][] rows)
    {
        var ws = workbook.Worksheets.Add(name);
        ws.ShowGridLines = false;

        ws.Cell(1, 1).Value = title;
        ws.Range(1, 1, 1, Headers.Length).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(ExcelStyling.Primary);
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 28;

        for (var i = 0; i < Headers.Length; i++)
            ws.Cell(2, i + 1).Value = Headers[i];
        ExcelStyling.ApplyHeaderStyle(ws.Range(2, 1, 2, Headers.Length));
        ws.Row(2).Height = 26;

        var row = 3;
        var zebra = false;
        foreach (var data in rows)
        {
            for (var c = 0; c < data.Length && c < Headers.Length; c++)
                ws.Cell(row, c + 1).Value = data[c];

            var line = ws.Range(row, 1, row, Headers.Length);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.SetFontColor(ExcelStyling.Primary);
            ws.Cell(row, 2).Style.Font.SetBold();
            ExcelStyling.WrapColumns(ws.Range(row, 3, row, 4));
            ws.Row(row).Height = 30;
            zebra = !zebra;
            row++;
        }

        // 预留若干空行，方便直接粘贴数据（空行导入时会被自动忽略）
        var blankStart = row;
        for (var i = 0; i < 8; i++)
        {
            var line = ws.Range(row, 1, row, Headers.Length);
            ExcelStyling.ApplyRowStyle(line, zebra);
            ws.Row(row).Height = 22;
            zebra = !zebra;
            row++;
        }
        // 空行无需斑马纹以外的格式，去掉多余描边保持整洁
        ws.Range(blankStart, 1, row - 1, Headers.Length).Style.Fill.SetBackgroundColor(XLColor.NoColor);

        for (var c = 0; c < ColumnWidths.Length; c++)
            ws.Column(c + 1).Width = ColumnWidths[c];
        ExcelStyling.NormalizeFont(ws);
        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.SetRowsToRepeatAtTop(1, 2);
    }
}
