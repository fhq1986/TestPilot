using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 动作枚举的取值契约 + 步骤值的解析规则。
///
/// 这里第一个测试是**防呆而不是防错**：ActionType 按 int 落库
/// （TestSteps.ActionType / ExecutionResults），既有数值一旦被改动或插入，
/// 历史用例的动作会**整体错位**——点一下变成输入、断言变成点击，而且不会报任何错。
/// 明细（TestStep.Config）是 jsonb，动它没事；动枚举数值才是致命的。
/// </summary>
public class StepActionContractTests
{
    [Fact]
    public void 既有动作的数值不得变动()
    {
        Assert.Equal(0, (int)ActionType.Click);
        Assert.Equal(1, (int)ActionType.Fill);
        Assert.Equal(2, (int)ActionType.Navigate);
        Assert.Equal(3, (int)ActionType.Wait);
        Assert.Equal(4, (int)ActionType.Screenshot);
        Assert.Equal(5, (int)ActionType.Scroll);
        Assert.Equal(6, (int)ActionType.Request);
        Assert.Equal(7, (int)ActionType.AssertResponse);
        Assert.Equal(8, (int)ActionType.ExtractVariable);
        Assert.Equal(9, (int)ActionType.AIAction);
        Assert.Equal(10, (int)ActionType.AIAssert);
        Assert.Equal(11, (int)ActionType.AssertVisible);
        Assert.Equal(12, (int)ActionType.AssertText);
        Assert.Equal(13, (int)ActionType.AssertUrl);
        Assert.Equal(14, (int)ActionType.AssertTitle);
        Assert.Equal(15, (int)ActionType.AssertA11y);
    }

    [Fact]
    public void 追加的动作必须排在末尾()
    {
        Assert.Equal(16, (int)ActionType.Select);
        Assert.Equal(17, (int)ActionType.UploadFile);
        Assert.Equal(18, (int)ActionType.PressKey);
        Assert.Equal(19, (int)ActionType.Hover);
        Assert.Equal(20, (int)ActionType.AssertAttribute);
        Assert.Equal(21, (int)ActionType.AssertCount);
        Assert.Equal(22, (int)ActionType.AssertValue);
        Assert.Equal(23, (int)ActionType.AssertState);
    }

    // ------------------------------ 上传文件路径解析

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void 空的文件路径列表解析为空(string? value)
        => Assert.Empty(ConfigValueParser.SplitFilePaths(value));

    [Fact]
    public void 换行分隔的文件路径()
    {
        var paths = ConfigValueParser.SplitFilePaths("C:\\a\\1.xlsx\nC:\\a\\2.png");
        Assert.Equal(new[] { "C:\\a\\1.xlsx", "C:\\a\\2.png" }, paths);
    }

    [Fact]
    public void 分号分隔的文件路径()
    {
        // 手写时常用分号；而多选复制路径时带的是换行——两种都得认
        var paths = ConfigValueParser.SplitFilePaths("/data/a.xlsx;/data/b.png");
        Assert.Equal(new[] { "/data/a.xlsx", "/data/b.png" }, paths);
    }

    [Fact]
    public void 混合分隔与首尾空白都会被清理()
    {
        var paths = ConfigValueParser.SplitFilePaths("  /data/a.xlsx ;\n\n  /data/b.png  ");
        Assert.Equal(new[] { "/data/a.xlsx", "/data/b.png" }, paths);
    }

    [Fact]
    public void 空项被丢弃()
    {
        var paths = ConfigValueParser.SplitFilePaths("/data/a.xlsx\n\n;\n/data/b.png");
        Assert.Equal(2, paths.Length);
    }

    /// <summary>
    /// 重复路径**不**去重：验证"重复上传同一文件"的业务规则是合法用例
    /// </summary>
    [Fact]
    public void 重复路径被保留()
    {
        var paths = ConfigValueParser.SplitFilePaths("/data/a.xlsx;/data/a.xlsx");
        Assert.Equal(2, paths.Length);
    }
}
