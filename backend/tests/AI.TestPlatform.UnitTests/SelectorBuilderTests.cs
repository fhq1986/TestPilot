using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Application.AI;

namespace AI.TestPlatform.UnitTests;

public class SelectorBuilderTests
{
    [Fact]
    public void Build_WithId_PrefersIdSelector()
    {
        var element = new InteractiveElement(0, "input", "username", "form-input", "", null);

        var selector = SelectorBuilder.Build(element);
        Assert.NotNull(selector);

        Assert.Equal("css", selector.Type);
        Assert.Equal("[id=\"username\"]", selector.Value);
    }

    [Fact]
    public void Build_WithAria_UsesAriaSelector()
    {
        var element = new InteractiveElement(1, "button", null, "btn", "搜索", "search-button");

        var selector = SelectorBuilder.Build(element);
        Assert.NotNull(selector);

        Assert.Equal("css", selector.Type);
        Assert.Equal("[aria-label=\"search-button\"]", selector.Value);
    }

    [Fact]
    public void Build_WithTextOnly_UsesXPathWithPosition()
    {
        var element = new InteractiveElement(2, "button", null, null, "登 录", null);

        var selector = SelectorBuilder.Build(element);
        Assert.NotNull(selector);

        Assert.Equal("xpath", selector.Type);
        Assert.Equal("(//button[normalize-space()=\"登 录\"])[1]", selector.Value);
    }

    [Fact]
    public void Build_WithNothing_ReturnsNull()
    {
        var element = new InteractiveElement(3, "div", null, null, "", null);

        var selector = SelectorBuilder.Build(element);

        Assert.Null(selector);
    }

    [Fact]
    public void Build_IdWithSpecialChars_UsesAttributeForm()
    {
        var element = new InteractiveElement(4, "input", "user:name", null, "", null);

        var selector = SelectorBuilder.Build(element);
        Assert.NotNull(selector);

        Assert.Equal("[id=\"user:name\"]", selector.Value);
    }

    [Fact]
    public void Build_TextWithDoubleQuote_UsesConcat()
    {
        var element = new InteractiveElement(5, "button", null, null, "确认\"退出\"", null);

        var selector = SelectorBuilder.Build(element);
        Assert.NotNull(selector);

        Assert.Equal("xpath", selector.Type);
        Assert.Contains("concat", selector.Value);
    }
}
