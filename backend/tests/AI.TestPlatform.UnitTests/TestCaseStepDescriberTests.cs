using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class TestCaseStepDescriberTests
{
    private static TestStep Step(int order, ActionType type, StepConfig config) =>
        new() { StepOrder = order, ActionType = type, Config = config };

    [Fact]
    public void 操作步骤_优先使用导入原文()
    {
        var testCase = new TestCase
        {
            SourceSteps = "输入用户名密码后点击登录",
            Steps = { Step(0, ActionType.Navigate, new StepConfig { Url = "/login" }) },
        };

        Assert.Equal("输入用户名密码后点击登录", TestCaseStepDescriber.ResolveSourceSteps(testCase));
    }

    [Fact]
    public void 操作步骤_无原文时按步骤生成可读文本()
    {
        var testCase = new TestCase
        {
            Steps =
            {
                Step(0, ActionType.Navigate, new StepConfig { Url = "/login" }),
                Step(1, ActionType.Fill, new StepConfig
                {
                    Selector = new SelectorConfig { Type = "css", Value = "input[name=username]", Description = "用户名输入框" },
                    Value = "admin",
                }),
                Step(2, ActionType.Click, new StepConfig
                {
                    Selector = new SelectorConfig { Type = "css", Value = "button.login", Description = "登录按钮" },
                }),
                Step(3, ActionType.Wait, new StepConfig { Value = "1000" }),
            },
        };

        var text = TestCaseStepDescriber.ResolveSourceSteps(testCase);

        Assert.Contains("1. 打开 /login", text);
        Assert.Contains("2. 在「用户名输入框」输入「admin」", text);
        Assert.Contains("3. 点击「登录按钮」", text);
        Assert.Contains("4. 等待 1000 毫秒", text);
    }

    [Fact]
    public void 操作步骤_选择器无描述时回退到选择器值()
    {
        var testCase = new TestCase
        {
            Steps =
            {
                Step(0, ActionType.Click, new StepConfig
                {
                    Selector = new SelectorConfig { Type = "css", Value = "#submit" },
                }),
            },
        };

        Assert.Contains("点击「#submit」", TestCaseStepDescriber.ResolveSourceSteps(testCase));
    }

    [Fact]
    public void 预期结果_优先使用导入原文()
    {
        var testCase = new TestCase
        {
            ExpectedResult = "提示用户名或密码错误",
            Steps = { Step(0, ActionType.AssertVisible, new StepConfig { Selector = new SelectorConfig { Description = "错误提示" } }) },
        };

        Assert.Equal("提示用户名或密码错误", TestCaseStepDescriber.ResolveExpectedResult(testCase));
    }

    [Fact]
    public void 预期结果_无原文时由断言步骤归纳()
    {
        var testCase = new TestCase
        {
            Steps =
            {
                Step(0, ActionType.AssertText, new StepConfig
                {
                    Selector = new SelectorConfig { Description = "校验提示" },
                    Value = "请输入密码",
                }),
                Step(1, ActionType.AssertUrl, new StepConfig { Value = "/home" }),
            },
        };

        var text = TestCaseStepDescriber.ResolveExpectedResult(testCase);

        Assert.Equal("「校验提示」显示「请输入密码」；页面地址包含「/home」", text);
    }

    [Fact]
    public void 预期结果_无断言步骤时为空()
    {
        var testCase = new TestCase
        {
            Steps = { Step(0, ActionType.Click, new StepConfig { Selector = new SelectorConfig { Value = "#a" } }) },
        };

        Assert.Equal(string.Empty, TestCaseStepDescriber.ResolveExpectedResult(testCase));
    }
}
