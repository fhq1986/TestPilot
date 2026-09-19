using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Domain.Entities;
using ClosedXML.Excel;

namespace AI.TestPlatform.UnitTests;

public class ExcelTestCaseParserTests
{
    /// <summary>构造一个最小可解析的用例工作簿（含需跳过的概览表）。</summary>
    private static MemoryStream BuildWorkbook(Action<XLWorkbook> build)
    {
        var workbook = new XLWorkbook();
        build(workbook);
        var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    private static void WriteModuleSheet(XLWorkbook workbook, string name, string title, params string[][] rows)
    {
        var ws = workbook.Worksheets.Add(name);
        ws.Cell(1, 1).Value = title;
        var headers = new[] { "用例编号", "测试场景", "操作步骤", "预期结果", "优先级", "类别" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cell(2, i + 1).Value = headers[i];
        var r = 3;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
                ws.Cell(r, c + 1).Value = row[c];
            r++;
        }
    }

    [Fact]
    public void Parse_读取模块与用例行_并跳过概览表()
    {
        using var stream = BuildWorkbook(wb =>
        {
            // 概览表应被跳过
            wb.Worksheets.Add("测试概览").Cell(1, 1).Value = "MES 测试用例";
            WriteModuleSheet(wb, "1. 登录认证模块", "1. 登录认证模块 — 用户登录",
                new[] { "TC-LOGIN-001", "正常登录", "输入正确的用户名密码", "返回 JWT Token", "高", "功能" },
                new[] { "TC-LOGIN-002", "空字段验证", "用户名或密码为空", "前端校验阻止提交", "中", "功能" });
            wb.Worksheets.Add("测试执行记录").Cell(1, 1).Value = "执行记录";
        });

        var parsed = ExcelTestCaseParser.Parse(stream);

        Assert.Single(parsed.Modules);
        Assert.Equal("1. 登录认证模块", parsed.Modules[0].Name);
        Assert.Equal("1. 登录认证模块 — 用户登录", parsed.Modules[0].Title);
        Assert.Equal(2, parsed.TotalRows);

        var first = parsed.Modules[0].Rows[0];
        Assert.Equal("TC-LOGIN-001", first.CaseCode);
        Assert.Equal("正常登录", first.Scenario);
        Assert.Equal("输入正确的用户名密码", first.SourceSteps);
        Assert.Equal("返回 JWT Token", first.Expected);
        Assert.Equal("P0", first.Priority);
        Assert.Equal(TestType.Web, first.Type);

        Assert.Equal("P1", parsed.Modules[0].Rows[1].Priority);
    }

    [Fact]
    public void Parse_表头顺序不同也能识别列()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("2. 订单模块");
            ws.Cell(1, 1).Value = "2. 订单模块";
            // 表头打乱顺序 + 使用同类异名
            ws.Cell(2, 1).Value = "预期结果";
            ws.Cell(2, 2).Value = "用例名称";
            ws.Cell(2, 3).Value = "前置条件/操作步骤";
            ws.Cell(2, 4).Value = "优先级";
            ws.Cell(3, 1).Value = "创建成功";
            ws.Cell(3, 2).Value = "创建订单";
            ws.Cell(3, 3).Value = "填写必填项后提交";
            ws.Cell(3, 4).Value = "低";
        });

        var parsed = ExcelTestCaseParser.Parse(stream);
        var row = parsed.Modules[0].Rows[0];
        Assert.Equal("创建订单", row.Scenario);
        Assert.Equal("创建成功", row.Expected);
        Assert.Equal("填写必填项后提交", row.SourceSteps);
        Assert.Equal("P2", row.Priority);
        // 无用例编号列时回退生成
        Assert.False(string.IsNullOrWhiteSpace(row.CaseCode));
    }

    [Fact]
    public void Parse_类别为接口时识别为Api用例()
    {
        using var stream = BuildWorkbook(wb =>
            WriteModuleSheet(wb, "3. 接口模块", "3. 接口模块",
                new[] { "TC-API-001", "查询订单详情", "调用 GET /api/orders/1", "返回订单信息", "高", "接口" }));

        var parsed = ExcelTestCaseParser.Parse(stream);
        Assert.Equal(TestType.Api, parsed.Modules[0].Rows[0].Type);
    }

    [Fact]
    public void Parse_缺少标题行与用例行时抛出可读错误()
    {
        using var stream = BuildWorkbook(wb =>
        {
            var ws = wb.Worksheets.Add("说明页");
            ws.Cell(1, 1).Value = "本文件仅用于说明";
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ExcelTestCaseParser.Parse(stream));
        Assert.Contains("模板格式", ex.Message);
    }

    [Theory]
    [InlineData("高", "P0")]
    [InlineData("中", "P1")]
    [InlineData("低", "P2")]
    [InlineData("P0", "P0")]
    [InlineData("p2", "P2")]
    [InlineData("High", "P0")]
    [InlineData("", "P2")]
    [InlineData("未知级别", "P2")]
    public void MapPriority_按模板优先级归一化(string raw, string expected)
        => Assert.Equal(expected, ExcelTestCaseParser.MapPriority(raw));

    [Fact]
    public void Parse_空白行被跳过()
    {
        using var stream = BuildWorkbook(wb =>
            WriteModuleSheet(wb, "4. 模块", "4. 模块",
                new[] { "TC-1", "有场景", "步骤", "预期", "高", "功能" },
                new[] { "", "", "", "", "", "" },
                new[] { "TC-3", "另一条", "步骤", "预期", "中", "功能" }));

        var parsed = ExcelTestCaseParser.Parse(stream);
        Assert.Equal(2, parsed.TotalRows);
    }

    [Fact]
    public void Template_生成的导入模板可被解析器正确读回()
    {
        // 模板必须与解析器保持同一结构，否则用户下载后无法导入
        using var stream = new MemoryStream(TestCaseTemplateBuilder.Build());

        var parsed = ExcelTestCaseParser.Parse(stream);

        Assert.Equal(2, parsed.Modules.Count);
        Assert.Equal(5, parsed.TotalRows);   // 登录模块 3 条 + 订单模块 2 条
        // 「填写说明」表必须被跳过
        Assert.DoesNotContain(parsed.Modules, m => m.Name.Contains("说明"));

        var login = parsed.Modules.First(m => m.Name.Contains("登录"));
        Assert.Equal("TC-LOGIN-001", login.Rows[0].CaseCode);
        Assert.Equal("正常登录", login.Rows[0].Scenario);
        Assert.Equal("P0", login.Rows[0].Priority);
        Assert.Equal(TestType.Web, login.Rows[0].Type);

        // 类别含「接口」的用例识别为 Api
        var order = parsed.Modules.First(m => m.Name.Contains("订单"));
        Assert.Equal(TestType.Api, order.Rows[0].Type);
        Assert.Equal("P1", order.Rows[1].Priority);
    }

    [Fact]
    public void Template_包含填写说明表与两个示例模块()
    {
        using var stream = new MemoryStream(TestCaseTemplateBuilder.Build());
        using var workbook = new XLWorkbook(stream);

        Assert.Contains("填写说明", workbook.Worksheets.Select(w => w.Name));
        Assert.Contains(workbook.Worksheets, w => w.Name.Contains("登录"));
        Assert.EndsWith(".xlsx", TestCaseTemplateBuilder.FileName);
    }
}
