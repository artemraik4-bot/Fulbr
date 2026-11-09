using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fulbr.Core.Models;
using Fulbr.Core.Parsing;
using Fulbr.Core.Util;

namespace Fulbr.Core.Engine;

/// <summary>
/// Builds a minimal sing-box configuration for proxy mode (no TUN, no system routes).
/// It never accepts insecure TLS settings and never passes through arbitrary JSON.
/// </summary>
public sealed class SingBoxConfigBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool TryBuild(
        ServerCandidate server,
        int listenPort,
        string username,
        string password,
        out string json,
        out string? error)
    {
        json = string.Empty;
        error = null;

        if (!TryBuildBase(server, listenPort, username, password, out var config, out error))
            return false;

        json = JsonSerializer.Serialize(config, JsonOptions);
        return true;
    }

    /// <summary>
    /// Builds a TUN-mode configuration. The actual TUN adapter is created by sing-box
    /// using the official Wintun DLL; this method only produces a validated config.
    /// </summary>
    public bool TryBuildTun(
        ServerCandidate server,
        TunOptions options,
        out string json,
        out string? error)
    {
        json = string.Empty;
        error = null;

        if (options.DiagnosticPort is < 1 or > 65535)
        {
            error = "Diagnostic port is invalid.";
            return false;
        }

        if (!TryBuildBase(server, options.DiagnosticPort, options.DiagnosticUsername,
                options.DiagnosticPassword, out var baseConfig, out error))
            return false;

        try
        {
            var baseNode = System.Text.Json.Nodes.JsonNode.Parse(
                JsonSerializer.Serialize(baseConfig, JsonOptions));
            var proxy = baseNode?["outbounds"]?[0]?.DeepClone();
            if (proxy is null)
            {
                error = "Proxy outbound could not be generated.";
                return false;
            }

            var inbounds = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = options.InterfaceName,
                    ["address"] = new System.Text.Json.Nodes.JsonArray
                    {
                        options.Address4,
                        options.Address6
                    },
                    ["mtu"] = options.Mtu,
                    ["auto_route"] = options.AutoRoute,
                    ["strict_route"] = options.StrictRoute,
                    ["stack"] = options.Stack
                }
            };

            if (options.DiagnosticPort > 0)
            {
                inbounds.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "diag-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = options.DiagnosticPort,
                    ["users"] = new System.Text.Json.Nodes.JsonArray
                    {
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["username"] = options.DiagnosticUsername,
                            ["password"] = options.DiagnosticPassword
                        }
                    }
                });
            }

            var root = new System.Text.Json.Nodes.JsonObject
            {
                ["log"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["level"] = "warn",
                    ["timestamp"] = true
                },
                ["dns"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["servers"] = new System.Text.Json.Nodes.JsonArray
                    {
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "local",
                            ["tag"] = "local-dns"
                        },
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["type"] = "https",
                            ["tag"] = "tun-dns",
                            ["server"] = options.DnsServer,
                            ["detour"] = "proxy",
                            ["domain_resolver"] = "local-dns"
                        }
                    },
                    ["final"] = "tun-dns",
                    ["strategy"] = "prefer_ipv4"
                },
                ["inbounds"] = inbounds,
                ["outbounds"] = new System.Text.Json.Nodes.JsonArray
                {
                    proxy,
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "direct",
                        ["tag"] = "direct"
                    }
                },
                ["route"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["auto_detect_interface"] = true,
                    ["default_domain_resolver"] = "local-dns",
                    ["final"] = "proxy",
                    ["rules"] = new System.Text.Json.Nodes.JsonArray
                    {
                        new System.Text.Json.Nodes.JsonObject { ["action"] = "sniff" },
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["protocol"] = "dns",
                            ["action"] = "hijack-dns"
                        },
                        new System.Text.Json.Nodes.JsonObject
                        {
                            ["ip_is_private"] = true,
                            ["outbound"] = "direct"
                        }
                    }
                }
            };

            if (options.ClashApiPort > 0 && !string.IsNullOrWhiteSpace(options.ClashApiSecret))
            {
                root["experimental"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["clash_api"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["external_controller"] = $"127.0.0.1:{options.ClashApiPort}",
                        ["secret"] = options.ClashApiSecret
                    }
                };
            }

            json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (Exception ex)
        {
            error = $"TUN config generation failed: {ex.GetType().Name}.";
            return false;
        }
    }

    private bool TryBuildBase(
        ServerCandidate server,
        int listenPort,
        string username,
        string password,
        out Dictionary<string, object?> config,
        out string? error)
    {
        config = null!;
        error = null;

        if (server.InsecureTls)
        {
            error = "This configuration disables TLS verification; Fulbr refuses it.";
            return false;
        }

        if (server.Protocol is ProxyProtocol.Hysteria2 or ProxyProtocol.Tuic)
        {
            error = $"{server.ProtocolName} is not supported yet.";
            return false;
        }

        if (!TryParse(server.RawLink, server.Protocol, out var details, out error))
            return false;

        if (details.Insecure)
        {
            error = "This configuration disables TLS verification; Fulbr refuses it.";
            return false;
        }

        Dictionary<string, object?> proxy;
        try
        {
            proxy = details.Protocol switch
            {
                ProxyProtocol.Vless => BuildVless(details),
                ProxyProtocol.Trojan => BuildTrojan(details),
                ProxyProtocol.Shadowsocks => BuildShadowsocks(details),
                ProxyProtocol.Vmess => BuildVmess(details),
                _ => throw new NotSupportedException($"Protocol {details.Protocol} is not supported.")
            };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = "warn",
                ["timestamp"] = true
            },
            ["inbounds"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = listenPort,
                    ["users"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["username"] = username,
                            ["password"] = password
                        }
                    }
                }
            },
            ["outbounds"] = new object[]
            {
                proxy,
                new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new Dictionary<string, object?>
            {
                ["final"] = "proxy"
            }
        };

        return true;
    }

    private static Dictionary<string, object?> BuildVless(ProxyLinkDetails d)
    {
        if (string.IsNullOrWhiteSpace(d.UserInfo))
            throw new InvalidOperationException("VLESS UUID is missing.");

        var security = (d.Security ?? string.Empty).ToLowerInvariant();
        if (security is "none" or "")
            throw new InvalidOperationException("VLESS without TLS/REALITY is refused in proxy mode.");

        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = d.Host,
            ["server_port"] = d.Port,
            ["uuid"] = d.UserInfo
        };

        if (!string.IsNullOrWhiteSpace(d.Flow))
            outbound["flow"] = d.Flow;

        if (!string.Equals(d.Network, "tcp", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(d.Network))
        {
            var transport = BuildTransport(d);
            if (transport is not null)
                outbound["transport"] = transport;
        }

        outbound["tls"] = BuildTls(d, reality: security == "reality");
        return outbound;
    }

    private static Dictionary<string, object?> BuildTrojan(ProxyLinkDetails d)
    {
        if (string.IsNullOrWhiteSpace(d.UserInfo))
            throw new InvalidOperationException("Trojan password is missing.");

        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "trojan",
            ["tag"] = "proxy",
            ["server"] = d.Host,
            ["server_port"] = d.Port,
            ["password"] = d.UserInfo,
            ["tls"] = BuildTls(d, reality: false)
        };

        var transport = BuildTransport(d);
        if (transport is not null)
            outbound["transport"] = transport;

        return outbound;
    }

    private static Dictionary<string, object?> BuildShadowsocks(ProxyLinkDetails d)
    {
        if (string.IsNullOrWhiteSpace(d.Method) || string.IsNullOrWhiteSpace(d.Password))
            throw new InvalidOperationException("Shadowsocks method or password is missing.");

        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "shadowsocks",
            ["tag"] = "proxy",
            ["server"] = d.Host,
            ["server_port"] = d.Port,
            ["method"] = d.Method,
            ["password"] = d.Password
        };

        if (!string.IsNullOrWhiteSpace(d.Plugin))
        {
            outbound["plugin"] = d.Plugin;
            if (!string.IsNullOrWhiteSpace(d.PluginOpts))
                outbound["plugin_opts"] = d.PluginOpts;
        }

        return outbound;
    }

    private static Dictionary<string, object?> BuildVmess(ProxyLinkDetails d)
    {
        if (string.IsNullOrWhiteSpace(d.UserInfo))
            throw new InvalidOperationException("VMess UUID is missing.");

        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "vmess",
            ["tag"] = "proxy",
            ["server"] = d.Host,
            ["server_port"] = d.Port,
            ["uuid"] = d.UserInfo,
            ["security"] = string.IsNullOrWhiteSpace(d.VmessSecurity) ? "auto" : d.VmessSecurity,
            ["alter_id"] = d.AlterId
        };

        if (!string.IsNullOrWhiteSpace(d.Security) &&
            d.Security.Equals("tls", StringComparison.OrdinalIgnoreCase))
            outbound["tls"] = BuildTls(d, reality: false);

        var transport = BuildTransport(d);
        if (transport is not null)
            outbound["transport"] = transport;

        return outbound;
    }

    private static Dictionary<string, object?> BuildTls(ProxyLinkDetails d, bool reality)
    {
        var tls = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["insecure"] = false
        };

        if (!string.IsNullOrWhiteSpace(d.Sni))
            tls["server_name"] = d.Sni;

        if (!string.IsNullOrWhiteSpace(d.Alpn))
            tls["alpn"] = d.Alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (reality)
        {
            if (string.IsNullOrWhiteSpace(d.PublicKey))
                throw new InvalidOperationException("REALITY public key (pbk) is missing.");

            tls["utls"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["fingerprint"] = string.IsNullOrWhiteSpace(d.Fingerprint) ? "chrome" : d.Fingerprint
            };
            tls["reality"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["public_key"] = d.PublicKey,
                ["short_id"] = d.ShortId ?? string.Empty
            };
        }
        else if (!string.IsNullOrWhiteSpace(d.Fingerprint))
        {
            tls["utls"] = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["fingerprint"] = d.Fingerprint
            };
        }

        return tls;
    }

    private static Dictionary<string, object?>? BuildTransport(ProxyLinkDetails d)
    {
        var network = (d.Network ?? string.Empty).ToLowerInvariant();
        switch (network)
        {
            case "":
            case "tcp":
                return null;
            case "ws":
            case "websocket":
                var ws = new Dictionary<string, object?>
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrWhiteSpace(d.Path) ? "/" : d.Path
                };
                if (!string.IsNullOrWhiteSpace(d.HostHeader))
                    ws["headers"] = new Dictionary<string, object?> { ["Host"] = d.HostHeader };
                return ws;
            case "grpc":
                return new Dictionary<string, object?>
                {
                    ["type"] = "grpc",
                    ["service_name"] = string.IsNullOrWhiteSpace(d.ServiceName)
                        ? d.Path ?? string.Empty
                        : d.ServiceName
                };
            case "h2":
            case "http":
                var http = new Dictionary<string, object?>
                {
                    ["type"] = "http",
                    ["path"] = string.IsNullOrWhiteSpace(d.Path) ? "/" : d.Path
                };
                if (!string.IsNullOrWhiteSpace(d.HostHeader))
                    http["host"] = new[] { d.HostHeader };
                return http;
            case "httpupgrade":
                return new Dictionary<string, object?>
                {
                    ["type"] = "httpupgrade",
                    ["host"] = d.HostHeader ?? string.Empty,
                    ["path"] = string.IsNullOrWhiteSpace(d.Path) ? "/" : d.Path
                };
            default:
                throw new InvalidOperationException($"Transport '{network}' is not supported in proxy mode yet.");
        }
    }

    private static bool TryParse(string rawLink, ProxyProtocol protocol, out ProxyLinkDetails details,
        out string? error)
    {
        details = null!;
        error = null;

        if (protocol == ProxyProtocol.Vmess)
            return TryParseVmess(rawLink, out details, out error);
        if (protocol == ProxyProtocol.Shadowsocks)
            return TryParseShadowsocks(rawLink, out details, out error);

        if (!TrySplitLink(rawLink, out var scheme, out var authority, out var query, out _))
        {
            error = "Malformed share link.";
            return false;
        }

        if (!TryParseAuthority(authority, out var userInfo, out var host, out var port, out error))
            return false;

        var q = ParseQuery(query);
        var security = Get(q, "security") ?? Get(q, "tls");
        var insecure = IsTrue(Get(q, "allowinsecure", "allow_insecure", "insecure", "skip-cert-verify", "skip_cert_verify"));

        details = new ProxyLinkDetails(
            protocol, host, port, userInfo, null, null,
            security,
            Get(q, "sni", "peer", "servername"),
            Get(q, "type", "net"),
            Get(q, "path"),
            Get(q, "host") ?? Get(q, "hostheader"),
            Get(q, "servicename", "service_name"),
            Get(q, "flow"),
            Get(q, "fp", "fingerprint"),
            Get(q, "pbk", "publickey", "public_key"),
            Get(q, "sid", "shortid", "short_id"),
            Get(q, "alpn"),
            insecure,
            null, 0, null, null);
        return true;
    }

    private static bool TryParseVmess(string rawLink, out ProxyLinkDetails details, out string? error)
    {
        details = null!;
        error = null;
        var payload = rawLink["vmess://".Length..].Trim();
        var hash = payload.IndexOf('#');
        if (hash >= 0) payload = payload[..hash];

        if (!ShareLinkParser.TryBase64Decode(payload, out var json))
        {
            error = "VMess payload is not valid Base64.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            var host = GetString(root, "add");
            var portText = GetString(root, "port");
            if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portText, out var port))
            {
                error = "VMess address or port is invalid.";
                return false;
            }

            var id = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                error = "VMess UUID is missing.";
                return false;
            }

            var network = GetString(root, "net");
            var path = GetString(root, "path");
            var tls = GetString(root, "tls");
            var hostHeader = GetString(root, "host");
            var sni = GetString(root, "sni");
            if (string.IsNullOrWhiteSpace(sni)) sni = hostHeader;
            if (string.IsNullOrWhiteSpace(network)) network = "tcp";

            var alterIdText = GetString(root, "aid");
            _ = int.TryParse(alterIdText, out var alterId);

            details = new ProxyLinkDetails(
                ProxyProtocol.Vmess, host, port, id, null, null,
                string.IsNullOrWhiteSpace(tls) ? null : tls,
                sni, network, path, hostHeader, null, null, null, null, null, null,
                false, GetString(root, "scy"), alterId, null, null);
            return true;
        }
        catch (JsonException)
        {
            error = "VMess JSON is malformed.";
            return false;
        }
    }

    private static bool TryParseShadowsocks(string rawLink, out ProxyLinkDetails details, out string? error)
    {
        details = null!;
        error = null;
        var rest = rawLink["ss://".Length..];
        var hash = rest.IndexOf('#');
        if (hash >= 0) rest = rest[..hash];
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
            var user = rest[..at];
            if (!TryParseHostPort(rest[(at + 1)..], out host, out port, out error))
                return false;

            var decoded = TryBase64Decode(user, out var b64) ? b64 : Uri.UnescapeDataString(user);
            var colon = decoded.IndexOf(':');
            if (colon <= 0 || colon == decoded.Length - 1)
            {
                error = "Shadowsocks method:password is malformed.";
                return false;
            }
            method = decoded[..colon];
            password = decoded[(colon + 1)..];
        }
        else
        {
            if (!TryBase64Decode(rest, out var decoded))
            {
                error = "Shadowsocks payload is not valid Base64.";
                return false;
            }
            var at = decoded.LastIndexOf('@');
            if (at <= 0)
            {
                error = "Shadowsocks authority is malformed.";
                return false;
            }
            var user = decoded[..at];
            var colon = user.IndexOf(':');
            if (colon <= 0 || colon == user.Length - 1)
            {
                error = "Shadowsocks method:password is malformed.";
                return false;
            }
            method = user[..colon];
            password = user[(colon + 1)..];
            if (!TryParseHostPort(decoded[(at + 1)..], out host, out port, out error))
                return false;
        }

        var q = ParseQuery(query);
        details = new ProxyLinkDetails(
            ProxyProtocol.Shadowsocks, host, port, null, method, password,
            null, null, null, null, null, null, null, null, null, null, null,
            false, null, 0, Get(q, "plugin"), Get(q, "plugin-opts", "plugin_opts"));
        return true;
    }

    private static bool TrySplitLink(string link, out string scheme, out string authority,
        out string query, out string fragment)
    {
        scheme = authority = query = fragment = string.Empty;
        var index = link.IndexOf("://", StringComparison.Ordinal);
        if (index <= 0) return false;
        scheme = link[..index];
        var rest = link[(index + 3)..];
        var hash = rest.IndexOf('#');
        if (hash >= 0)
        {
            fragment = rest[(hash + 1)..];
            rest = rest[..hash];
        }
        var q = rest.IndexOf('?');
        if (q >= 0)
        {
            query = rest[(q + 1)..];
            rest = rest[..q];
        }
        authority = rest;
        return authority.Length > 0;
    }

    private static bool TryParseAuthority(string authority, out string? userInfo, out string host,
        out int port, out string? error)
    {
        userInfo = null;
        host = string.Empty;
        port = 0;
        error = null;
        var at = authority.LastIndexOf('@');
        var hostPart = authority;
        if (at >= 0)
        {
            userInfo = Uri.UnescapeDataString(authority[..at]);
            hostPart = authority[(at + 1)..];
        }
        return TryParseHostPort(hostPart, out host, out port, out error);
    }

    private static bool TryParseHostPort(string text, out string host, out int port, out string? error)
    {
        host = string.Empty;
        port = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Missing host.";
            return false;
        }
        if (text[0] == '[')
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                error = "Unterminated IPv6 address.";
                return false;
            }
            host = text[1..close];
            var rest = text[(close + 1)..];
            if (!rest.StartsWith(':') || !int.TryParse(rest[1..], out port) || port is < 1 or > 65535)
            {
                error = "Invalid IPv6 port.";
                return false;
            }
            return true;
        }
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1 ||
            !int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535)
        {
            error = "Invalid host or port.";
            return false;
        }
        host = text[..colon];
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return result;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
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
        if (!root.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty
        };
    }

    private static bool TryBase64Decode(string value, out string result)
        => ShareLinkParser.TryBase64Decode(value, out result);
}
