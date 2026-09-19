using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class TestCaseMappingTests
{
    [Fact]
    public void ToDto_MapsAllFieldsAndSteps()
    {
        var testCase = new TestCase
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Name = "登录用例",
            Type = TestType.Web,
            Status = TestCaseStatus.Active,
            Timeout = 30000,
        };
        testCase.Steps.Add(new TestStep
        {
            StepOrder = 1,
            ActionType = ActionType.Click,
            Config = new StepConfig { Url = "https://example.com" },
        });
        testCase.Steps.Add(new TestStep
        {
            StepOrder = 0,
            ActionType = ActionType.Navigate,
        });

        var dto = testCase.ToDto();

        Assert.Equal("登录用例", dto.Name);
        Assert.Equal(2, dto.Steps.Count);
        Assert.Equal(TestType.Web, dto.Type);
        Assert.Equal("https://example.com", dto.Steps[0].Config.Url);
    }
}
