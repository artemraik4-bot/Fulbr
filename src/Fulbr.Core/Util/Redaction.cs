using System.Text.RegularExpressions;

namespace Fulbr.Core.Util;

public static class Redaction
{
    private static readonly Regex Uuid = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly Regex SecretParams = new(
        @"(?i)(password|passwd|pwd|token|uuid|id|auth|pbk|sid|key)=([^&#\s]+)",
        RegexOptions.Compiled);

    public static string RedactPreview(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
            return string.Empty;

        var schemeIndex = link.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex <= 0)
            return Truncate(SecretParams.Replace(Uuid.Replace(link, "<uuid>"), "$1=<redacted>"), 80);

        var scheme = link[..schemeIndex];
        var rest = link[(schemeIndex + 3)..];

        var hash = rest.IndexOf('#');
        if (hash >= 0)
            rest = rest[..hash];

        var query = rest.IndexOf('?');
        if (query >= 0)
            rest = rest[..query];

        var at = rest.LastIndexOf('@');
        var hostPart = at >= 0 ? rest[(at + 1)..] : rest;
        return $"{scheme}://<redacted>@{Truncate(hostPart, 60)}";
    }

    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "<invalid-url>";
        return $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}";
    }

    public static string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var result = Uuid.Replace(text, "<uuid>");
        result = SecretParams.Replace(result, "$1=<redacted>");
        return result;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
