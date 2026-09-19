using AI.TestPlatform.Api.Mocks;

namespace AI.TestPlatform.UnitTests;

public class SemanticExampleGeneratorTests
{
    [Theory]
    [InlineData("id", "1001")]
    [InlineData("userId", "1001")]
    [InlineData("email", "user@example.com")]
    [InlineData("price", 99.99)]
    [InlineData("phone", "13800138000")]
    [InlineData("createdAt", "2026-01-01T00:00:00")]
    [InlineData("name", "示例名称")]
    public void Generate_KeywordAware_ProducesSemanticValue(string field, object expected)
    {
        var value = SemanticExampleGenerator.Generate(field, "string");

        Assert.Equal(expected.ToString(), value.ToString());
    }

    [Fact]
    public void Generate_Integer_ReturnsZero()
    {
        Assert.Equal(0L, SemanticExampleGenerator.Generate("count", "integer"));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("paid")]
    public void Generate_NonIdKeywords_NotMisclassified(string field)
    {
        Assert.Equal("string", SemanticExampleGenerator.Generate(field, "string"));
    }

    [Fact]
    public void Generate_Boolean_ReturnsTrue()
    {
        Assert.True((bool)SemanticExampleGenerator.Generate("enabled", "boolean"));
    }
}
