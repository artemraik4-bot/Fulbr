using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fulbr.Core.Models;
using Fulbr.Core.Util;

namespace Fulbr.Core.Parsing;

/// <summary>
/// Strict parser for the share-link formats used by public subscriptions.
/// It never executes content and never follows references. Unknown or malformed
/// entries are rejected with a reason and a redacted preview.
/// </summary>
public sealed class ShareLinkParser
{
    private const int MaxLines = 20_000;
    private const int MaxLineLength = 16_384;

    private static readonly HashSet<string> SecureSsCiphers = new(StringComparer.OrdinalIgnoreCase)
    {
        "aes-128-gcm", "aes-256-gcm",
        "chacha20-ietf-poly1305", "xchacha20-ietf-poly1305",
        "2022-blake3-aes-128-gcm", "2022-blake3-aes-256-gcm",
        "2022-blake3-chacha20-poly1305"
    };

    private static readonly Regex UuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    public ParseOutcome Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ParseOutcome.Empty;

        if (content.Length > 8 * 1024 * 1024)
            return new ParseOutcome(Array.Empty<ServerCandidate>(),
                new[] { new RejectedConfig("Content exceeds the 8 MiB parser limit.", "<too large>") });

        var text = content.Trim();

        // Many subscriptions are a single Base64 blob containing share links.
        if (!text.Contains("://", StringComparison.Ordinal) && TryBase64Decode(text, out var decoded))
            text = decoded;

        var servers = new List<ServerCandidate>();
        var rejected = new List<RejectedConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > MaxLines)
            return new ParseOutcome(Array.Empty<ServerCandidate>(),
                new[] { new RejectedConfig($"Too many lines (>{MaxLines}).", "<truncated>") });

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.Length > MaxLineLength)
            {
                rejected.Add(new RejectedConfig("Line exceeds the 16 KiB limit.", "<too long>"));
                continue;
            }

            var parsed = TryParseOne(line, out var candidate, out var reason);
            if (!parsed || candidate is null)
            {
                rejected.Add(new RejectedConfig(reason ?? "Unsupported or malformed configuration.",
                    Redaction.RedactPreview(line)));
                continue;
            }

            if (!seen.Add(candidate.Fingerprint))
                continue; // duplicate inside this subscription

            servers.Add(candidate);
        }

        return new ParseOutcome(servers, rejected);
    }

    public bool TryParseOne(string link, out ServerCandidate? candidate, out string? reason)
    {
        candidate = null;
        reason = null;

        try
        {
            if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                return TryParseVmess(link, out candidate, out reason);

            if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                return TryParseShadowsocks(link, out candidate, out reason);

            if (!TrySplitLink(link, out var scheme, out var authority, out var query, out var fragment))
            {
                reason = "Missing or invalid scheme/authority.";
                return false;
            }

            var protocol = scheme.ToLowerInvariant() switch
            {
                "vless" => ProxyProtocol.Vless,
                "trojan" => ProxyProtocol.Trojan,
                "hysteria2" or "hy2" => ProxyProtocol.Hysteria2,
                "tuic" => ProxyProtocol.Tuic,
                "socks" or "socks5" => ProxyProtocol.Socks5,
                _ => ProxyProtocol.Unknown
            };

            if (protocol == ProxyProtocol.Unknown)
            {
                reason = $"Unsupported scheme '{scheme}'.";
                return false;
            }

            if (!TryParseAuthority(authority, out var userInfo, out var host, out var port, out reason))
                return false;

            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            {
                reason = "Invalid host or port.";
                return false;
            }

            var q = ParseQuery(query);
            var name = string.IsNullOrWhiteSpace(fragment)
                ? $"{host}:{port}"
                : Uri.UnescapeDataString(fragment);
            name = SanitizeName(name, host, port);

            var warnings = new List<string>();
            var sni = Get(q, "sni", "peer", "serverName");
            var transport = Get(q, "type", "net");
            var flow = Get(q, "flow");
            var security = Get(q, "security") ?? Get(q, "tls");
            var insecure = IsTrue(Get(q, "allowinsecure", "allow_insecure", "insecure", "skip-cert-verify", "skip_cert_verify"));

            if (insecure)
                warnings.Add("TLS certificate verification is disabled by the configuration.");

            if (protocol is ProxyProtocol.Vless or ProxyProtocol.Vmess or ProxyProtocol.Trojan)
            {
                if (string.Equals(security, "none", StringComparison.OrdinalIgnoreCase))
                    warnings.Add("Transport encryption is disabled (security=none).");
                else if (string.IsNullOrWhiteSpace(security))
                    warnings.Add("TLS/REALITY was not specified; the endpoint may be plaintext.");
                else if (security is not null &&
                         !security.Equals("tls", StringComparison.OrdinalIgnoreCase) &&
                         !security.Equals("reality", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Unknown security mode '{security}'.");
            }

            if (protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic)
            {
                if (string.IsNullOrWhiteSpace(security))
                    warnings.Add("QUIC/TLS is expected for this protocol; verify the configuration.");
            }

            var credential = userInfo ?? string.Empty;
            var credentialHash = string.IsNullOrEmpty(credential) ? null : Hashing.HashSecret(credential);
            if (string.IsNullOrEmpty(credential))
                warnings.Add("No credential found in the link.");

            var tlsEnabled = protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic ||
                             string.Equals(security, "tls", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(security, "reality", StringComparison.OrdinalIgnoreCase) ||
                             (protocol == ProxyProtocol.Trojan && !string.Equals(security, "none", StringComparison.OrdinalIgnoreCase));

            var fingerprint = Hashing.Fingerprint(
                protocol.ToString(), host.ToLowerInvariant(), port.ToString(CultureInfo.InvariantCulture),
                credentialHash ?? string.Empty, transport, security, sni, flow);

            candidate = new ServerCandidate(
                protocol, host, port, name, link, fingerprint, credentialHash,
                sni, transport, flow, tlsEnabled, insecure, warnings);
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Parser error: {ex.GetType().Name}.";
            return false;
        }
    }

    private static bool TryParseVmess(string link, out ServerCandidate? candidate, out string? reason)
    {
        candidate = null;
        reason = null;
        var payload = link["vmess://".Length..].Trim();
        var hashIndex = payload.IndexOf('#');
        if (hashIndex >= 0)
            payload = payload[..hashIndex];

        if (!TryBase64Decode(payload, out var json))
        {
            reason = "VMess payload is not valid Base64.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            var host = GetString(root, "add");
            var portText = GetString(root, "port");
            if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portText, out var port) || port is < 1 or > 65535)
            {
                reason = "VMess JSON has an invalid address or port.";
                return false;
            }

            var id = GetString(root, "id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                reason = "VMess JSON has no user id.";
                return false;
            }

            var name = GetString(root, "ps");
            name = string.IsNullOrWhiteSpace(name) ? $"{host}:{port}" : SanitizeName(name, host, port);
            var tls = GetString(root, "tls");
            var network = GetString(root, "net");
            var sni = GetString(root, "sni") ?? GetString(root, "host");
            var warnings = new List<string>();
            var tlsEnabled = string.Equals(tls, "tls", StringComparison.OrdinalIgnoreCase);
            if (!tlsEnabled)
                warnings.Add("VMess link has no TLS; transport is encrypted by VMess AEAD only.");
            if (string.IsNullOrWhiteSpace(network))
                network = "tcp";

            var credentialHash = Hashing.HashSecret(id);
            var fingerprint = Hashing.Fingerprint(
                "Vmess", host.ToLowerInvariant(), port.ToString(CultureInfo.InvariantCulture),
                credentialHash, network, tls, sni, GetString(root, "path"));

            candidate = new ServerCandidate(
                ProxyProtocol.Vmess, host, port, name, link, fingerprint, credentialHash,
                sni, network, null, tlsEnabled, false, warnings);
            return true;
        }
        catch (JsonException)
        {
            reason = "VMess JSON is malformed or exceeds the depth limit.";
            return false;
        }
    }

    private static bool TryParseShadowsocks(string link, out ServerCandidate? candidate, out string? reason)
    {
        candidate = null;
        reason = null;
        var rest = link["ss://".Length..];
        var fragment = string.Empty;
        var hashIndex = rest.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = rest[(hashIndex + 1)..];
            rest = rest[..hashIndex];
        }

        var query = string.Empty;
        var qIndex = rest.IndexOf('?');
        if (qIndex >= 0)
        {
            query = rest[(qIndex + 1)..];
            rest = rest[..qIndex];
        }

        string method;
        string password;
        string host;
        int port;

        if (rest.Contains('@'))
        {
            var at = rest.LastIndexOf('@');
            var userPart = rest[..at];
            var hostPart = rest[(at + 1)..];

            if (!TryParseHostPort(hostPart, out host, out port, out reason))
                return false;

            var decodedUser = DecodeBase64OrPlain(userPart);
            var colon = decodedUser.IndexOf(':');
            if (colon <= 0 || colon == decodedUser.Length - 1)
            {
                reason = "Shadowsocks user info is not in method:password form.";
                return false;
            }

            method = decodedUser[..colon];
            password = decodedUser[(colon + 1)..];
        }
        else
        {
            if (!TryBase64Decode(rest, out var decoded))
            {
                reason = "Shadowsocks payload is not valid Base64.";
                return false;
            }

            var at = decoded.LastIndexOf('@');
            if (at <= 0)
            {
                reason = "Shadowsocks payload has no authority.";
                return false;
            }

            var userPart = decoded[..at];
            var hostPart = decoded[(at + 1)..];
            var colon = userPart.IndexOf(':');
            if (colon <= 0 || colon == userPart.Length - 1)
            {
                reason = "Shadowsocks payload is not in method:password@host:port form.";
                return false;
            }

            method = userPart[..colon];
            password = userPart[(colon + 1)..];
            if (!TryParseHostPort(hostPart, out host, out port, out reason))
                return false;
        }

        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrEmpty(password))
        {
            reason = "Shadowsocks cipher or password is missing.";
            return false;
        }

        var warnings = new List<string>();
        if (!SecureSsCiphers.Contains(method))
        {
            reason = $"Shadowsocks cipher '{method}' is not in the secure allow-list.";
            return false;
        }

        var q = ParseQuery(query);
        if (q.ContainsKey("plugin"))
            warnings.Add("Shadowsocks plugin is not supported in this version; plugin parameters were ignored.");

        var name = string.IsNullOrWhiteSpace(fragment) ? $"{host}:{port}" : Uri.UnescapeDataString(fragment);
        name = SanitizeName(name, host, port);
        var credentialHash = Hashing.HashSecret(password);
        var fingerprint = Hashing.Fingerprint(
            "Shadowsocks", host.ToLowerInvariant(), port.ToString(CultureInfo.InvariantCulture),
            method, credentialHash, Get(q, "plugin"));

        candidate = new ServerCandidate(
            ProxyProtocol.Shadowsocks, host, port, name, link, fingerprint, credentialHash,
            null, null, null, false, false, warnings);
        return true;
    }

    private static bool TrySplitLink(string link, out string scheme, out string authority,
        out string query, out string fragment)
    {
        scheme = authority = query = fragment = string.Empty;
        var schemeIndex = link.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex <= 0)
            return false;

        scheme = link[..schemeIndex];
        var rest = link[(schemeIndex + 3)..];

        var hashIndex = rest.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = rest[(hashIndex + 1)..];
            rest = rest[..hashIndex];
        }

        var queryIndex = rest.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = rest[(queryIndex + 1)..];
            rest = rest[..queryIndex];
        }

        authority = rest;
        return !string.IsNullOrWhiteSpace(authority);
    }

    private static bool TryParseAuthority(string authority, out string? userInfo, out string host,
        out int port, out string? reason)
    {
        userInfo = null;
        host = string.Empty;
        port = 0;
        reason = null;

        var at = authority.LastIndexOf('@');
        var hostPart = authority;
        if (at >= 0)
        {
            userInfo = Uri.UnescapeDataString(authority[..at]);
            hostPart = authority[(at + 1)..];
        }

        return TryParseHostPort(hostPart, out host, out port, out reason);
    }

    private static bool TryParseHostPort(string hostPart, out string host, out int port, out string? reason)
    {
        host = string.Empty;
        port = 0;
        reason = null;

        if (string.IsNullOrWhiteSpace(hostPart))
        {
            reason = "Missing host.";
            return false;
        }

        if (hostPart[0] == '[')
        {
            var close = hostPart.IndexOf(']');
            if (close < 0)
            {
                reason = "Unterminated IPv6 address.";
                return false;
            }

            host = hostPart[1..close];
            var remainder = hostPart[(close + 1)..];
            if (remainder.StartsWith(':') && int.TryParse(remainder[1..], out port))
                return true;

            reason = "Missing IPv6 port.";
            return false;
        }

        var colon = hostPart.LastIndexOf(':');
        if (colon <= 0 || colon == hostPart.Length - 1)
        {
            reason = "Missing host or port.";
            return false;
        }

        host = hostPart[..colon];
        if (!int.TryParse(hostPart[(colon + 1)..], out port) || port is < 1 or > 65535)
        {
            reason = "Invalid port.";
            return false;
        }

        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string? Get(Dictionary<string, string> q, params string[] keys)
    {
        foreach (var key in keys)
            if (q.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static bool IsTrue(string? value) =>
        value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty
        };
    }

    private static string SanitizeName(string name, string host, int port)
    {
        name = UuidRegex.Replace(name, "<redacted>");
        return DisplayText.CleanName(name, $"{host}:{port}");
    }

    private static string DecodeBase64OrPlain(string value)
    {
        var decoded = Uri.UnescapeDataString(value);
        return TryBase64Decode(decoded, out var result) ? result : decoded;
    }

    internal static bool TryBase64Decode(string value, out string result)
    {
        result = string.Empty;
        try
        {
            var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray())
                .Replace('-', '+').Replace('_', '/');
            if (compact.Length == 0)
                return false;

            switch (compact.Length % 4)
            {
                case 2: compact += "=="; break;
                case 3: compact += "="; break;
                case 1: return false;
            }

            var bytes = Convert.FromBase64String(compact);
            // Reject binary payloads; only accept valid UTF-8 text.
            result = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return result.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
