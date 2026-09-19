using AI.TestPlatform.Api.Audit;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 审计摘要的脱敏与截断。
///
/// 这块之前一直没有测试，而它是**安全的最后一道**：请求与响应都要落库，
/// 任何一次漏脱敏都会把密码或令牌永久写进审计表——而审计表恰恰是所有管理员都能看的。
///
/// 另一个被忽视的失效模式是"截断没截干净"：超过列宽会让 Postgres 报 22001，
/// 而审计写入失败是被 catch 掉的，于是变成**静默丢记录**——
/// 你以为记下了，其实那一条根本不存在。所以下面有一条专门钉住长度上界。
/// </summary>
public class AuditLogServiceTests
{
    // ------------------------------ 脱敏

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("newPassword")]
    [InlineData("token")]
    [InlineData("accessToken")]
    [InlineData("refreshToken")]
    [InlineData("apiKey")]
    [InlineData("webhookToken")]
    [InlineData("smtpPassword")]
    [InlineData("clientSecret")]
    public void 敏感字段被替换为星号(string key)
    {
        var result = AuditLogService.Sanitize($$"""{"{{key}}":"should-not-leak"}""");

        Assert.DoesNotContain("should-not-leak", result!);
        Assert.Contains("***", result);
    }

    [Fact]
    public void 嵌套对象与数组里的敏感字段同样被脱敏()
    {
        var json = """
            {"user":{"name":"张三","password":"p@ss"},"tokens":["t1"],"list":[{"apiKey":"k1"}]}
            """;

        var result = AuditLogService.Sanitize(json);

        Assert.DoesNotContain("p@ss", result!);
        Assert.DoesNotContain("k1", result);
        Assert.Contains("张三", result);
    }

    /// <summary>
    /// 响应体是**我们主动采集**的新增内容，必须确认它走的是同一把脱敏尺子。
    /// 登录接口虽然没挂审计，但"改了以后有人给 /login 加上 WithAudit"是完全可能的，
    /// 那时响应里的 JWT 就必须被 *** 掉。
    /// </summary>
    [Fact]
    public void 登录形态的响应体里令牌不会泄露()
    {
        var json = """
            {"token":"eyJhbGciOiJIUzI1NiJ9.payload.sig","user":{"username":"admin","passwordHash":"abc"}}
            """;

        var result = AuditLogService.Sanitize(json);

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", result!);
        Assert.DoesNotContain("abc", result);
        Assert.Contains("admin", result);
    }

    /// <summary>
    /// 兜底路径覆盖的不只是"带引号的 JSON 残留"，还有 urlencoded 表单与自由文本。
    /// 后者早先完全没被处理过——`password=p@ss` 会原样落库。
    /// </summary>
    [Theory]
    [InlineData("password: \"p@ss\"")]              // 带引号（截断的 JSON 残留）
    [InlineData("password=\"p@ss\"")]               // 赋值 + 引号（multipart 字段）
    [InlineData("password=p@ss&user=admin")]        // urlencoded 表单
    [InlineData("token: abc123")]                   // 自由文本
    public void 非JSON文本同样会被脱敏(string body)
    {
        var result = AuditLogService.Sanitize(body);

        Assert.Contains("***", result!);
        Assert.DoesNotContain("p@ss", result);
        Assert.DoesNotContain("abc123", result);
    }

    /// <summary>兜底脱敏只动敏感名，别把审计内容本身毁掉</summary>
    [Fact]
    public void 兜底脱敏不误伤无关字段()
    {
        var result = AuditLogService.Sanitize("password=p@ss&user=admin&module=登录");

        Assert.Contains("admin", result!);
        Assert.Contains("登录", result);
        Assert.DoesNotContain("p@ss", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空值原样返回(string? value) => Assert.Equal(value, AuditLogService.Sanitize(value));

    // ------------------------------ 截断

    [Fact]
    public void 短响应原样保留()
    {
        const string json = """{"id":"1","name":"ok"}""";
        Assert.Equal(json, AuditLogService.CapResponse(json));
    }

    [Fact]
    public void 刚好等于列宽的响应不被改动()
    {
        // 前缀 `{"v":"` 是 6 字符，后缀 `"}` 是 2 字符，故补 3992 个恰好凑满 4000
        var json = "{\"v\":\"" + new string('x', 3992) + "\"}";
        Assert.Equal(4000, json.Length);          // 前提成立才有意义
        Assert.Equal(json, AuditLogService.CapResponse(json));
    }

    /// <summary>
    /// 超长时必须换成**自描述信封**：能看出被截断了、能看出原始长度、原样不再是"看着像完整 JSON"。
    /// </summary>
    [Fact]
    public void 超长响应换成自描述信封()
    {
        var json = "{\"v\":\"" + new string('x', 9000) + "\"}";

        var result = AuditLogService.CapResponse(json)!;

        Assert.Contains("\"truncated\":true", result);
        Assert.Contains($"\"originalLength\":{json.Length}", result);
        Assert.DoesNotContain(new string('x', 4000), result);   // 不再整段保留
        Assert.Contains(new string('x', 100), result);          // 但保留了预览
    }

    /// <summary>
    /// **不变量**：结果长度绝不能超过列宽 4000。
    /// 超了 → Postgres 22001 → 被 catch → 那条审计静默消失。
    /// 信封字段本身会占长度，且 JSON 转义还会让内容变长（引号、反斜杠、控制字符），
    /// 所以这里用"转义后最坏情况"的输入来试。
    /// </summary>
    [Theory]
    [InlineData('x')]      // 普通字符
    [InlineData('"')]      // 需要转义成 \"，长度翻倍
    [InlineData('\\')]     // 需要转义成 \\
    [InlineData('\n')]     // 需要转义成 \n
    public void 截断后的长度绝不超过列宽(char ch)
    {
        var json = "{\"v\":\"" + new string(ch, 20000) + "\"}";

        var result = AuditLogService.CapResponse(json)!;

        Assert.True(result.Length <= 4000, $"截断结果长度 {result.Length} 超过列宽 4000，会导致审计静默丢失");
    }

    [Fact]
    public void 截断是幂等的()
    {
        var json = "{\"v\":\"" + new string('x', 9000) + "\"}";

        var once = AuditLogService.CapResponse(json);
        Assert.Equal(once, AuditLogService.CapResponse(once));
    }

    // ------------------------------ 通用截断：绝不能写出比列更长的值

    /// <summary>
    /// **这是 2026-09-17 真实踩到的 bug**：原实现是 <c>value[..max] + "..."</c>，
    /// 实际长度 **max + 3**，超过列定义 → Postgres 22001 →
    /// 审计写入失败被 catch 掉 → **整条记录静默消失**。
    ///
    /// 现场证据（backend.log）：
    /// <c>审计日志写入失败：Create TestCase (null) / 22001: value too long for type character varying(2000)</c>
    /// 也就是说：只要请求体超过 2000 字符（带几步的用例创建/更新），
    /// 那条操作在审计里根本不存在——"以为记下了、其实没有"。
    /// </summary>
    [Theory]
    [InlineData(300)]     // ResourceName / UserAgent 列宽
    [InlineData(500)]     // Path 列宽
    [InlineData(2000)]    // Detail 列宽
    [InlineData(4000)]    // ResponseBody 列宽
    public void 截断结果绝不超过目标宽度(int max)
    {
        var result = AuditLogService.Truncate(new string('x', max * 3), max)!;

        Assert.True(result.Length <= max,
            $"截断到 {max} 却写出了 {result.Length} 个字符，会导致审计整条丢失");
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void 刚好等于宽度时不截断()
    {
        var value = new string('x', 2000);
        Assert.Equal(value, AuditLogService.Truncate(value, 2000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void 无需截断时原样返回(string? value)
        => Assert.Equal(value, AuditLogService.Truncate(value, 100));

    /// <summary>宽度比省略号还短时也不能越界（否则又是一个 22001）</summary>
    [Fact]
    public void 宽度小于省略号长度时也不越界()
        => Assert.True(AuditLogService.Truncate("abcdefghij", 2)!.Length <= 2);
}
