using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class PlaywrightStepMapperTests
{
    [Fact]
    public void ToLocator_CssSelector_ReturnsValue()
    {
        var selector = new SelectorConfig { Type = "css", Value = "#login-btn" };

        var result = PlaywrightStepMapper.ToLocator(selector);

        Assert.Equal("#login-btn", result);
    }

    [Fact]
    public void ToLocator_XPathSelector_ReturnsValue()
    {
        var selector = new SelectorConfig { Type = "xpath", Value = "//button[@id='go']" };

        var result = PlaywrightStepMapper.ToLocator(selector);

        Assert.Equal("//button[@id='go']", result);
    }

    [Fact]
    public void ToLocator_AiSelector_ThrowsM4NotSupported()
    {
        var selector = new SelectorConfig { Type = "ai", Description = "蓝色登录按钮" };

        var ex = Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ToLocator(selector));

        Assert.Contains("M4", ex.Message);
    }

    [Fact]
    public void ToLocator_UnknownType_Throws()
    {
        var selector = new SelectorConfig { Type = "jquery", Value = "#x" };

        Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ToLocator(selector));
    }

    [Fact]
    public void ToLocator_NoSelector_Throws()
    {
        Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ToLocator(null));
    }

    [Fact]
    public void ToLocator_MissingValue_Throws()
    {
        var selector = new SelectorConfig { Type = "css" };

        Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ToLocator(selector));
    }

    [Fact]
    public void ParseWaitMs_ValidNumber_ReturnsMilliseconds()
    {
        var result = PlaywrightStepMapper.ParseWaitMs("1500");

        Assert.Equal(1500, result);
    }

    [Fact]
    public void ParseWaitMs_InvalidValue_Throws()
    {
        Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ParseWaitMs("abc"));
    }

    [Fact]
    public void ParseWaitMs_NonPositiveValue_Throws()
    {
        Assert.Throws<StepExecutionException>(() => PlaywrightStepMapper.ParseWaitMs("0"));
    }
}
