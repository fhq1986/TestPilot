using AI.TestPlatform.Application.Reports;

namespace AI.TestPlatform.UnitTests;

public class BatchReportRequestValidatorTests
{
    private readonly BatchReportRequestValidator _validator = new();

    [Fact]
    public void 空选择校验失败()
    {
        var result = _validator.Validate(new BatchReportRequest(new List<Guid>()));
        Assert.False(result.IsValid);
        Assert.Contains("必须选择要导出的执行记录", result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void 超过上限校验失败()
    {
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();
        Assert.False(_validator.Validate(new BatchReportRequest(ids)).IsValid);
    }

    [Fact]
    public void 正常数量校验通过()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        Assert.True(_validator.Validate(new BatchReportRequest(ids)).IsValid);
    }
}
