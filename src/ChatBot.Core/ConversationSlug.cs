using System.Text;

namespace ChatBot;

/// <summary>
/// Derives stable, typeable conversation ids from human titles. Ids are lowercase
/// ASCII slugs so users can type them in <c>/switch</c> without quoting.
/// </summary>
public static class ConversationSlug
{
    /// <summary>
    /// Lowercases the title, replaces each run of non-alphanumeric characters with a single
    /// dash, and trims leading/trailing dashes. Returns an empty string when the title has no
    /// usable ASCII alphanumerics (the caller substitutes a fallback via <see cref="MakeUnique"/>).
    /// </summary>
    public static string Slugify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var sb = new StringBuilder(title.Length);
        bool pendingDash = false;
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0)
                    sb.Append('-');
                sb.Append(c);
                pendingDash = false;
            }
            else if (sb.Length > 0)
            {
                pendingDash = true;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns <paramref name="slug"/> if it is non-empty and not already taken; otherwise
    /// appends <c>-2</c>, <c>-3</c>… until unique. A blank slug falls back to <c>chat-1</c>,
    /// <c>chat-2</c>… Comparison against <paramref name="existingIds"/> is case-insensitive.
    /// </summary>
    public static string MakeUnique(string slug, IEnumerable<string> existingIds)
    {
        var taken = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        string normalized = (slug ?? string.Empty).ToLowerInvariant();
        bool blank = normalized.Length == 0;

        if (!blank && !taken.Contains(normalized))
            return normalized;

        string baseSlug = blank ? "chat" : normalized;
        int n = blank ? 1 : 2;
        while (taken.Contains($"{baseSlug}-{n}"))
            n++;
        return $"{baseSlug}-{n}";
    }
}
