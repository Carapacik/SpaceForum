using System.Text;

namespace SpaceForum.Domain.Discussions;

public static class TopicSlug
{
    public const int MaxLength = 180;

    public static string Create(string title)
    {
        var builder = new StringBuilder();
        var pendingSeparator = false;
        foreach (var rune in (title ?? string.Empty).Trim().ToLowerInvariant().EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(rune.ToString().Normalize(NormalizationForm.FormC));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = builder.Length > 0;
            }

            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        return builder.ToString().TrimEnd('-') is { Length: > 0 } slug ? slug : "topic";
    }
}
