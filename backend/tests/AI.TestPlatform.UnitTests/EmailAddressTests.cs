using AI.TestPlatform.Api.Common;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 邮箱判据的单测。
///
/// 这个类值得单独锁住，是因为它是**两个地方的同一把尺子**：
/// 用户表单的邮箱校验、以及发验收邮件时的收件人过滤。
/// 两边一旦分叉，症状是「表单存得进去、邮件却静默发不出去」——极难排查。
/// </summary>
public class EmailAddressTests
{
    [Theory]
    [InlineData("zhangsan@example.com")]
    [InlineData("a.b+c@sub.example.com.cn")]
    [InlineData("  spaced@example.com  ")]   // 前后空白会被 Trim
    public void 合法地址通过(string value)
        => Assert.True(EmailAddress.IsValid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]             // 没有 @
    [InlineData("缺@符号")]                   // 域名不带点 —— 曾经被 MailAddress 判成合法
    [InlineData("abc@.com")]                 // 点紧跟在 @ 后面
    [InlineData("abc@example.")]             // 以点结尾
    [InlineData("@example.com")]             // 没有本地部分
    [InlineData("张三 <a@b.com>")]            // 带显示名，字段语义是"邮箱"本身
    [InlineData("a b@example.com")]          // 本地部分含空格
    public void 非法地址被拒(string? value)
        => Assert.False(EmailAddress.IsValid(value));

    [Fact]
    public void 超长地址被拒()
        => Assert.False(EmailAddress.IsValid(new string('a', 195) + "@example.com"));

    [Fact]
    public void 列表解析会丢弃非法项而不是整体失败()
    {
        // 发信是尽力而为的旁路流程：某人少写一个 @ 不该让整封报告发不出去
        var result = EmailAddress.ParseList("a@x.com, 坏地址 , b@y.com;A@X.COM");

        Assert.Equal(new[] { "a@x.com", "b@y.com" }, result);
    }

    [Fact]
    public void 列表解析去重忽略大小写()
        => Assert.Single(EmailAddress.ParseList("A@x.com,a@X.com"));

    [Fact]
    public void 列表解析限量()
        => Assert.Equal(3, EmailAddress.ParseList(
            "a@x.com,b@x.com,c@x.com,d@x.com,e@x.com", max: 3).Count);

    [Fact]
    public void 空列表返回空集合()
        => Assert.Empty(EmailAddress.ParseList("  ,, ; "));
}
