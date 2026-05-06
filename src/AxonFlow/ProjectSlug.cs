using System.Globalization;
using System.Text;

namespace AxonFlow;

internal static class ProjectSlug
{
    /// <summary>Slug from cwd directory name when <c>--project</c> is omitted.</summary>
    public static string InferFromWorkingDirectory()
    {
        try
        {
            var cwd = Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var leaf = Path.GetFileName(cwd);
            var normalized = Normalize(leaf);
            return string.IsNullOrEmpty(normalized) ? "default" : normalized;
        }
        catch
        {
            return "default";
        }
    }

    /// <summary>Lowercase slug; alphanumeric and hyphen only; collapses separators.</summary>
    public static string Normalize(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(Math.Max(s.Length, 4));
        var prevDash = false;
        foreach (var ch in s)
        {
            var ok = char.IsAsciiLetterOrDigit(ch);
            var dash = ok ? ch : '-';
            if (dash == '-')
            {
                if (!prevDash) sb.Append('-');
                prevDash = true;
            }
            else
            {
                sb.Append(ch);
                prevDash = false;
            }
        }
        var trimmed = sb.ToString().Trim('-');
        return trimmed.Length == 0 ? "" : TrimMaxLength(trimmed, 96);
    }

    public static string DisplayNameFromSlug(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return slug;
        var ti = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(' ', parts.Select(p => ti.ToTitleCase(p)));
    }

    private static string TrimMaxLength(string slug, int max)
    {
        if (slug.Length <= max) return slug;
        return slug[..max].TrimEnd('-');
    }
}
