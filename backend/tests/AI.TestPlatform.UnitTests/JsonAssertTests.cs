using System.Text.Json;
using AI.TestPlatform.Application.ApiTesting;

namespace AI.TestPlatform.UnitTests;

public class JsonAssertTests
{
    [Fact]
    public void IsSubset_ExpectationContainedInActual_True()
    {
        var actual = JsonDocument.Parse("""{"id":1,"name":"张三","extra":{"a":1,"b":2}}""");
        var expected = JsonDocument.Parse("""{"name":"张三","extra":{"b":2}}""");

        var result = JsonAssert.IsSubset(expected.RootElement, actual.RootElement, out var mismatch);

        Assert.True(result);
        Assert.Null(mismatch);
    }

    [Fact]
    public void IsSubset_ValueMismatch_FalseWithPath()
    {
        var actual = JsonDocument.Parse("""{"name":"张三"}""");
        var expected = JsonDocument.Parse("""{"name":"李四"}""");

        var result = JsonAssert.IsSubset(expected.RootElement, actual.RootElement, out var mismatch);

        Assert.False(result);
        Assert.Contains("name", mismatch);
    }

    [Fact]
    public void IsSubset_ArrayElementContained_True()
    {
        var actual = JsonDocument.Parse("""{"items":[{"id":1},{"id":2}]}""");
        var expected = JsonDocument.Parse("""{"items":[{"id":2}]}""");

        Assert.True(JsonAssert.IsSubset(expected.RootElement, actual.RootElement, out _));
    }

    [Fact]
    public void EvaluateJsonPath_DotAndIndex_Works()
    {
        var doc = JsonDocument.Parse("""{"data":{"items":[{"token":"abc"},{"token":"def"}]}}""");

        Assert.Equal("abc", JsonAssert.EvaluateJsonPath(doc.RootElement, "$.data.items[0].token"));
        Assert.Equal("def", JsonAssert.EvaluateJsonPath(doc.RootElement, "$.data.items[1].token"));
        Assert.Null(JsonAssert.EvaluateJsonPath(doc.RootElement, "$.data.nope"));
    }

    [Fact]
    public void ResolveVariables_MissingVariable_KeepsPlaceholder()
    {
        var result = JsonAssert.ResolveVariables("https://api.example.com/users/{missing}", new Dictionary<string, string>());

        Assert.Equal("https://api.example.com/users/{missing}", result);
    }

    [Theory]
    [InlineData("https://api.example.com/users/{id}", "id", "42", "https://api.example.com/users/42")]
    [InlineData("{\"token\":\"{token}\"}", "token", "abc", "{\"token\":\"abc\"}")]
    [InlineData("no placeholders", "x", "y", "no placeholders")]
    public void ResolveVariables_ReplacesPlaceholders(string template, string key, string value, string expected)
    {
        var vars = new Dictionary<string, string> { [key] = value };

        var result = JsonAssert.ResolveVariables(template, vars);

        Assert.Equal(expected, result);
    }
}
