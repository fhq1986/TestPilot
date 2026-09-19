using AI.TestPlatform.Application.Schedules;

namespace AI.TestPlatform.UnitTests;

public class CronUtilsTests
{
    [Theory]
    [InlineData("0 2 * * *")]
    [InlineData("*/5 * * * *")]
    [InlineData("30 1-5 * * 1-5")]
    [InlineData("0 0 1 * *")]
    [InlineData("0 9 * * 0")]
    public void 合法表达式通过校验(string expression) => Assert.True(CronUtils.IsValid(expression));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 2 * *")]              // 只有 4 段
    [InlineData("0 2 * * * *")]          // 6 段（带秒）不支持
    [InlineData("99 2 * * *")]           // 分钟越界
    [InlineData("0 25 * * *")]           // 小时越界
    [InlineData("abc")]
    public void 非法表达式被拒绝(string expression) => Assert.False(CronUtils.IsValid(expression));

    [Fact]
    public void 非法表达式给出可读原因()
    {
        Assert.Null(CronUtils.TryParse("0 2 * *", out var error));
        Assert.NotNull(error);
        Assert.Contains("5 段", error);
    }

    [Theory]
    [InlineData("0 2 * * *", "每天 02:00")]
    [InlineData("*/5 * * * *", "每 5 分钟")]
    [InlineData("* * * * *", "每分钟")]
    [InlineData("0 * * * *", "每小时整点")]
    [InlineData("0 9 * * 1", "每周一 09:00")]
    [InlineData("30 8 * * 1-5", "工作日 08:30")]
    [InlineData("0 3 1 * *", "每月 1 日 03:00")]
    public void 常见表达式产出可读描述(string expression, string expected)
        => Assert.Equal(expected, CronUtils.Describe(expression));

    [Fact]
    public void 下次执行时间按本地时间解释且严格递增()
    {
        var from = new DateTime(2026, 9, 11, 1, 30, 0, DateTimeKind.Local);
        var occurrences = CronUtils.NextOccurrences("0 2 * * *", 3, from);

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateTime(2026, 9, 11, 2, 0, 0), occurrences[0]);
        Assert.Equal(new DateTime(2026, 9, 12, 2, 0, 0), occurrences[1]);
        Assert.Equal(DateTimeKind.Local, occurrences[0].Kind);
        Assert.True(occurrences[1] > occurrences[0]);
    }

    [Fact]
    public void 非法表达式没有下次执行时间()
    {
        Assert.Null(CronUtils.GetNextOccurrence("bogus"));
        Assert.Empty(CronUtils.NextOccurrences("bogus", 5));
    }
}
