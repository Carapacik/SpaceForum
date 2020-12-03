using SpaceForum.Web.Rendering;

namespace SpaceForum.IntegrationTests.Rendering;

public sealed class ForumMarkdownRendererTests
{
    private readonly ForumMarkdownRenderer renderer = new();

    [Fact]
    public void ToHtmlRendersCommonForumMarkdown()
    {
        var html = renderer.ToHtml("**bold**\n\n```csharp\nvar answer = 42;\n```");

        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-csharp\">", html, StringComparison.Ordinal);
        Assert.Contains("var answer = 42;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtmlDisablesRawHtmlAndDangerousLinks()
    {
        var html = renderer.ToHtml("<script>alert(1)</script> [unsafe](javascript:alert(2))");

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtmlKeepsHttpsAndRelativeLinks()
    {
        var html = renderer.ToHtml("[external](https://example.com) [local](/search)");

        Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/search\"", html, StringComparison.Ordinal);
    }
}
