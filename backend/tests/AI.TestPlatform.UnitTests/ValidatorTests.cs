using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

public class ProjectValidatorTests
{
    private readonly CreateProjectRequestValidator _createValidator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateProject_EmptyName_Fails(string name)
    {
        var result = _createValidator.Validate(new CreateProjectRequest(name, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateProject_NameTooLong_Fails()
    {
        var result = _createValidator.Validate(
            new CreateProjectRequest(new string('x', 101), null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateProject_Valid_Passes()
    {
        var result = _createValidator.Validate(
            new CreateProjectRequest("正常项目", "描述"));

        Assert.True(result.IsValid);
    }
}

public class TestCaseValidatorTests
{
    private readonly CreateTestCaseRequestValidator _validator = new();

    [Fact]
    public void CreateTestCase_TimeoutOutOfRange_Fails()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", TestType.Web, null, "chrome", 999, 0,
            new List<CreateTestStepRequest>(), null);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateTestCase_NullSteps_Fails()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", TestType.Web, null, "chrome", 30000, 0,
            null!, null);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateTestCase_BrowserTooLong_Fails()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", TestType.Web, null, new string('x', 51), 30000, 0,
            new List<CreateTestStepRequest>(), null);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateTestCase_InvalidEnum_Fails()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", (TestType)99, null, "chrome", 30000, 0,
            new List<CreateTestStepRequest>(), null);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateTestCase_Valid_Passes()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", TestType.Web, null, "chrome", 30000, 0,
            new List<CreateTestStepRequest>(), null);

        Assert.True(_validator.Validate(request).IsValid);
    }

    [Fact]
    public void CreateTestCase_BaseUrlTooLong_Fails()
    {
        var request = new CreateTestCaseRequest(
            Guid.NewGuid(), "用例", TestType.Api, null, null, 30000, 0,
            new List<CreateTestStepRequest>(), new string('x', 501));

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
    }
}
