using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace SpaceForum.Web.Rendering;

public sealed class ForumMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsSafeUrl(link.Url))
            {
                link.Url = "#";
                link.Title = null;
            }
        }

        return Markdown.ToHtml(document, Pipeline);
    }

    private static bool IsSafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri) || !uri.IsAbsoluteUri)
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
    }
}
