using AI.TestPlatform.Application.Common;

namespace AI.TestPlatform.UnitTests;

public class BatchDeleteRequestValidatorTests
{
    private readonly BatchDeleteRequestValidator _validator = new();

    [Fact]
    public void 空列表校验失败()
    {
        var result = _validator.Validate(new BatchDeleteRequest(new List<Guid>()));
        Assert.False(result.IsValid);
        Assert.Contains("必须指定要删除的记录", result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void 超过上限校验失败()
    {
        var ids = Enumerable.Range(0, BatchDeleteRequestValidator.MaxIds + 1)
            .Select(_ => Guid.NewGuid()).ToList();
        var result = _validator.Validate(new BatchDeleteRequest(ids));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("不能超过"));
    }

    [Fact]
    public void 正常数量校验通过()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        Assert.True(_validator.Validate(new BatchDeleteRequest(ids)).IsValid);
    }
}
