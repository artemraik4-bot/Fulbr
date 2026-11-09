using Fulbr.Core.Models;

namespace Fulbr.Core.Engine;

public sealed record ProxyLinkDetails(
    ProxyProtocol Protocol,
    string Host,
    int Port,
    string? UserInfo,
    string? Method,
    string? Password,
    string? Security,
    string? Sni,
    string? Network,
    string? Path,
    string? HostHeader,
    string? ServiceName,
    string? Flow,
    string? Fingerprint,
    string? PublicKey,
    string? ShortId,
    string? Alpn,
    bool Insecure,
    string? VmessSecurity,
    int AlterId,
    string? Plugin,
    string? PluginOpts);
