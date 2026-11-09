using System.Text.RegularExpressions;

namespace Fulbr.Core.Util;

/// <summary>
/// Configuration names are untrusted display strings. Remove control characters,
/// emoji/surrogate pairs that Windows desktop fonts render as boxes, and collapse
/// whitespace. Never use this for security decisions.
/// </summary>
public static class DisplayText
{
    private static readonly Regex Surrogates = new(@"[\uD800-\uDFFF]", RegexOptions.Compiled);
    private static readonly Regex Invisible = new(@"[\uFE00-\uFE0F\u200D\u20E3]", RegexOptions.Compiled);
    private static readonly Regex ControlOrFormat = new(@"[\p{Cc}\p{Cf}]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RepeatedPipes = new(@"(\|\s*){2,}", RegexOptions.Compiled);

    public static string CleanName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var value = Surrogates.Replace(name, string.Empty);
        value = Invisible.Replace(value, string.Empty);
        value = ControlOrFormat.Replace(value, string.Empty);
        value = Whitespace.Replace(value, " ");
        value = RepeatedPipes.Replace(value, "| ");
        value = value.Trim(' ', '|', '-', '·');
        if (value.Length == 0)
            return fallback;
        return value.Length > 80 ? value[..80] : value;
    }
}
