using AI.TestPlatform.Application.Scripts;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class PlaywrightScriptParserTests
{
    private const string CodegenScript = """
        import { test, expect } from '@playwright/test';

        test('登录流程', async ({ page }) => {
          await page.goto('https://example.com/login');
          await page.getByLabel('用户名').fill('admin');
          await page.getByPlaceholder('请输入密码').fill('Admin@123456');
          await page.getByRole('button', { name: '登录' }).click();
          await expect(page).toHaveURL(/dashboard/);
          await expect(page.getByText('欢迎回来')).toBeVisible();
          await expect(page.locator('.el-message')).toContainText('登录成功');
          await page.locator('#logout').click();
          await page.waitForTimeout(500);
          await page.screenshot({ path: 'done.png' });
        });
        """;

    [Fact]
    public void 解析codegen脚本并推断用例信息()
    {
        var result = PlaywrightScriptParser.Parse(CodegenScript);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("登录流程", result.SuggestedName);
        Assert.Equal(TestType.Web, result.SuggestedType);
        Assert.Equal("https://example.com", result.SuggestedBaseUrl);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void 动作类型与顺序正确()
    {
        var steps = PlaywrightScriptParser.Parse(CodegenScript).Steps;
        var actions = steps.Select(s => s.ActionType).ToList();

        Assert.Equal(new[]
        {
            ActionType.Navigate, ActionType.Fill, ActionType.Fill, ActionType.Click,
            ActionType.AssertUrl, ActionType.AssertVisible, ActionType.AssertText,
            ActionType.Click, ActionType.Wait, ActionType.Screenshot,
        }, actions);
    }

    [Fact]
    public void goto解析为Navigate并带URL()
    {
        var step = PlaywrightScriptParser.Parse(CodegenScript).Steps[0];
        Assert.Equal("https://example.com/login", step.Config.Url);
    }

    [Fact]
    public void 语义定位转换为AI描述定位()
    {
        var steps = PlaywrightScriptParser.Parse(CodegenScript).Steps;

        Assert.Equal("ai", steps[1].Config.Selector!.Type);   // getByLabel
        Assert.Equal("表单标签「用户名」", steps[1].Config.Selector!.Description);
        Assert.Equal("Admin@123456", steps[2].Config.Value);   // fill 的输入值
        Assert.Equal("ai", steps[2].Config.Selector!.Type);
        Assert.Equal("占位符「请输入密码」", steps[2].Config.Selector!.Description);
        Assert.Equal("ai", steps[3].Config.Selector!.Type);   // getByRole
        Assert.Equal("按钮「登录」", steps[3].Config.Selector!.Description);
        Assert.Equal("ai", steps[5].Config.Selector!.Type);   // getByText
        Assert.Equal("文本「欢迎回来」", steps[5].Config.Selector!.Description);
    }

    [Fact]
    public void css与xpath定位精确保留()
    {
        var script = """
            await page.locator('#logout').click();
            await page.locator('.el-button--primary').click();
            await page.locator('//div[@id="app"]//button').click();
            await page.locator('css=ul > li.item').click();
            await page.locator('[data-testid="submit"]').click();
            """;
        var steps = PlaywrightScriptParser.Parse(script).Steps;

        Assert.Equal("css", steps[0].Config.Selector!.Type);
        Assert.Equal("#logout", steps[0].Config.Selector!.Value);
        Assert.Equal(".el-button--primary", steps[1].Config.Selector!.Value);
        Assert.Equal("xpath", steps[2].Config.Selector!.Type);
        Assert.Equal("//div[@id=\"app\"]//button", steps[2].Config.Selector!.Value);
        Assert.Equal("css", steps[3].Config.Selector!.Type);
        Assert.Equal("ul > li.item", steps[3].Config.Selector!.Value);   // css= 前缀被剥掉
        Assert.Equal("[data-testid=\"submit\"]", steps[4].Config.Selector!.Value);
    }

    [Fact]
    public void 断言解析到正确的字段()
    {
        var steps = PlaywrightScriptParser.Parse(CodegenScript).Steps;

        Assert.Equal("dashboard", steps[4].Config.Value);          // toHaveURL 的正则字面量取内部内容
        Assert.Equal("登录成功", steps[6].Config.Value);            // toContainText
    }

    [Fact]
    public void 老式page方法写法也能解析()
    {
        var script = """
            await page.goto('https://example.com');
            await page.click('button.login');
            await page.fill('#username', 'admin');
            await page.type('input[name="pwd"]', 'secret');
            await page.keyboard.press('Enter');
            """;
        var result = PlaywrightScriptParser.Parse(script);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(ActionType.Click, result.Steps[1].ActionType);
        Assert.Equal("button.login", result.Steps[1].Config.Selector!.Value);
        Assert.Equal("admin", result.Steps[2].Config.Value);
        Assert.Equal("secret", result.Steps[3].Config.Value);
    }

    [Fact]
    public void API脚本识别为Api用例()
    {
        var script = """
            import { test, expect } from '@playwright/test';

            test('查询详情接口', async ({ request }) => {
              const response = await request.fetch('/api/projects/1', { method: 'GET' });
              expect(response.ok()).toBeTruthy();
            });
            """;
        var result = PlaywrightScriptParser.Parse(script);

        Assert.Equal(TestType.Api, result.SuggestedType);
    }

    [Fact]
    public void 不支持的行进入告警且不影响其它步骤()
    {
        var script = """
            await page.goto('https://example.com');
            await page.frameLocator('#frame').getByText('x').click();
            await page.setViewportSize({ width: 800, height: 600 });
            await page.getByTestId('ok').click();
            """;
        var result = PlaywrightScriptParser.Parse(script);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, result.Steps.Count);                 // goto + getByTestId
        Assert.Equal(2, result.Warnings.Count);              // frameLocator + setViewportSize
        Assert.Contains("frameLocator", string.Join('\n', result.Warnings));
        Assert.Equal("[data-testid=\"ok\"]", result.Steps[1].Config.Selector!.Value);
    }

    [Fact]
    public void 空脚本与无有效步骤给出可读错误()
    {
        Assert.False(PlaywrightScriptParser.Parse("").Ok);
        Assert.False(PlaywrightScriptParser.Parse("   ").Ok);

        var noStep = PlaywrightScriptParser.Parse("import { test } from '@playwright/test';");
        Assert.False(noStep.Ok);
        Assert.Contains("没有解析出", noStep.Error);
    }

    [Fact]
    public void press与近似方法给出note提示()
    {
        var script = """
            await page.locator('#search').press('Enter');
            await page.locator('select').selectOption('a');
            await page.locator('#box').check();
            """;
        var steps = PlaywrightScriptParser.Parse(script).Steps;

        Assert.Equal("Enter", steps[0].Config.Value);
        Assert.Contains("近似映射", steps[0].Note);
        Assert.Contains("selectOption", steps[1].Note);
        Assert.Contains("check", steps[2].Note);
    }

    [Theory]
    [InlineData("page.locator('#a')", "css", "#a")]
    [InlineData("page.locator('xpath=//div')", "xpath", "//div")]
    [InlineData("page.locator('text=确定')", "ai", null)]
    [InlineData("page.getByText('确定')", "ai", null)]
    [InlineData("page.getByLabel('用户名')", "ai", null)]
    [InlineData("page.getByRole('button', { name: '确定' })", "ai", null)]
    [InlineData("page.getByRole('link')", "ai", null)]
    public void 定位器解析矩阵(string expression, string expectedType, string? expectedValue)
    {
        var selector = PlaywrightScriptParser.ParseLocator(expression);

        Assert.NotNull(selector);
        Assert.Equal(expectedType, selector!.Type);
        Assert.Equal(expectedValue, selector.Value);
    }

    [Fact]
    public void 参数切分忽略字符串与括号内的逗号()
    {
        var parts = PlaywrightScriptParser.SplitArguments("'#a, b', 'c', { name: 'd, e' }");
        Assert.Equal(3, parts.Count);
        Assert.Equal("'#a, b'", parts[0]);
        Assert.Equal("{ name: 'd, e' }", parts[2]);
    }

    [Fact]
    public void 链式调用切分正确()
    {
        Assert.True(PlaywrightScriptParser.TrySplitCall(
            "page.locator('x').click()", out var target, out var method, out var args));
        Assert.Equal("page.locator('x')", target);
        Assert.Equal("click", method);
        Assert.Equal("", args);

        Assert.True(PlaywrightScriptParser.TrySplitCall(
            "expect(page).toHaveURL(/a/)", out target, out method, out args));
        Assert.Equal("expect(page)", target);
        Assert.Equal("toHaveURL", method);
        Assert.Equal("/a/", args);
    }
}

public class PlaywrightScriptExporterTests
{
    private static TestCase SampleWebCase() => new()
    {
        Name = "登录用例",
        CaseCode = "TC-001",
        Module = "登录",
        Priority = "P0",
        Type = TestType.Web,
        BaseUrl = "http://localhost:3000",
        Steps =
        {
            new TestStep { StepOrder = 0, ActionType = ActionType.Navigate, Config = new StepConfig { Url = "/login" } },
            new TestStep
            {
                StepOrder = 1, ActionType = ActionType.Fill,
                Config = new StepConfig
                {
                    Selector = new SelectorConfig { Type = "css", Value = "#username" },
                    Value = "admin",
                },
            },
            new TestStep
            {
                StepOrder = 2, ActionType = ActionType.Click,
                Config = new StepConfig { Selector = new SelectorConfig { Type = "ai", Description = "按钮「登录」" } },
            },
            new TestStep
            {
                StepOrder = 3, ActionType = ActionType.AssertUrl,
                Config = new StepConfig { Value = "/dashboard" },
            },
            new TestStep
            {
                StepOrder = 4, ActionType = ActionType.AssertText,
                Config = new StepConfig
                {
                    Selector = new SelectorConfig { Type = "css", Value = ".welcome" },
                    Value = "欢迎",
                },
            },
        },
    };

    [Fact]
    public void 导出Web用例为可运行脚本()
    {
        var script = PlaywrightScriptExporter.Export(SampleWebCase());

        Assert.Contains("import { test, expect } from '@playwright/test';", script);
        Assert.Contains("test('登录用例', async ({ page }) => {", script);
        Assert.Contains("await page.goto('/login');", script);
        Assert.Contains("await page.locator('#username').fill('admin');", script);
        Assert.Contains("await page.locator('.welcome')).toContainText", script.Replace("expect(", ""));
        Assert.Contains("await expect(page).toHaveURL(/\\/dashboard/);", script);
        Assert.Contains("// 模块：登录", script);
        Assert.Contains("// 优先级：P0", script);
    }

    [Fact]
    public void AI定位退化成语义定位并保留注释()
    {
        var script = PlaywrightScriptExporter.Export(SampleWebCase());
        Assert.Contains("getByText('按钮「登录」')", script);
        Assert.Contains("AI 定位：按钮「登录」", script);
    }

    [Fact]
    public void 导出API用例为request形式()
    {
        var testCase = new TestCase
        {
            Name = "接口用例",
            Type = TestType.Api,
            BaseUrl = "http://localhost:5000",
            Steps =
            {
                new TestStep
                {
                    StepOrder = 0, ActionType = ActionType.Request,
                    Config = new StepConfig
                    {
                        Method = "POST",
                        Endpoint = "/api/login",
                        Headers = new List<HeaderEntry> { new() { Name = "X-Trace", Value = "1" } },
                        Body = "{\"user\":\"a\"}",
                    },
                },
                new TestStep { StepOrder = 1, ActionType = ActionType.AssertResponse, Config = new StepConfig() },
            },
        };

        var script = PlaywrightScriptExporter.Export(testCase);

        Assert.Contains("async ({ request })", script);
        Assert.Contains("request.fetch('/api/login'", script);
        Assert.Contains("method: 'POST'", script);
        Assert.Contains("'X-Trace': '1'", script);
        Assert.Contains("expect(response.ok()).toBeTruthy();", script);
    }

    [Fact]
    public void 转义单引号与换行()
    {
        var testCase = new TestCase
        {
            Name = "带'引号'的用例",
            Type = TestType.Web,
            Steps =
            {
                new TestStep
                {
                    StepOrder = 0, ActionType = ActionType.Fill,
                    Config = new StepConfig
                    {
                        Selector = new SelectorConfig { Type = "css", Value = "#a" },
                        Value = "it's ok\n第二行",
                    },
                },
            },
        };

        var script = PlaywrightScriptExporter.Export(testCase);

        Assert.Contains("test('带\\'引号\\'的用例'", script);
        Assert.Contains("fill('it\\'s ok\\n第二行')", script);
    }

    [Fact]
    public void 导出结果能被解析器读回()
    {
        var script = PlaywrightScriptExporter.Export(SampleWebCase());
        var parsed = PlaywrightScriptParser.Parse(script);

        Assert.True(parsed.Ok, parsed.Error);
        // 导出的每个步骤都应能被解析回来（AI 定位会退化为文本定位）
        Assert.Equal(5, parsed.Steps.Count);
        Assert.Equal(ActionType.Navigate, parsed.Steps[0].ActionType);
        Assert.Equal(ActionType.AssertUrl, parsed.Steps[3].ActionType);
    }

    /// <summary>
    /// 录制器产出的脚本一定带 <c>test.use({ viewport: {...} })</c> 这段 codegen 样板。
    /// 跨多行的配置块必须整块跳过，否则块内每个属性行都会变成一条"未映射"告警，
    /// 用户每次录制都会看到一串毫无意义的噪声。
    /// </summary>
    private const string RecorderScript = """
        import { test, expect } from '@playwright/test';

        test.use({
          viewport: {
            height: 720,
            width: 1280
          }
        });

        test('test', async ({ page }) => {
          await page.goto('https://example.com/');
          await page.getByRole('link', { name: 'More information' }).click();
          await page.locator('#submit').click();
        });
        """;

    [Fact]
    public void 录制脚本的配置块不产生告警()
    {
        var result = PlaywrightScriptParser.Parse(RecorderScript);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Warnings);
        // 只应解析出真实操作，配置块内的属性行不能变成步骤
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal(ActionType.Navigate, result.Steps[0].ActionType);
        Assert.Equal("https://example.com/", result.Steps[0].Config.Url);
        Assert.Equal("https://example.com", result.SuggestedBaseUrl);
    }

    [Fact]
    public void 配置块后的步骤不会被吞掉()
    {
        // 回归保护：早期用朴素括号计数会把 test(...) 的函数体一并吞掉
        var result = PlaywrightScriptParser.Parse(RecorderScript);

        Assert.True(result.Ok, result.Error);
        Assert.Contains(result.Steps, s => s.Config.Selector?.Value == "#submit");
        Assert.All(result.Steps, s => Assert.DoesNotContain("viewport", s.SourceLine));
    }

    [Fact]
    public void 字符串字面量内的括号不影响块深度计算()
    {
        var script = """
            import { test } from '@playwright/test';
            test.use({ locale: 'zh-CN' });
            test('括号', async ({ page }) => {
              await page.locator('#a(1)').click();
              await page.locator('.b{2}').click();
            });
            """;

        var result = PlaywrightScriptParser.Parse(script);

        Assert.True(result.Ok, result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Steps.Count);
    }
}
