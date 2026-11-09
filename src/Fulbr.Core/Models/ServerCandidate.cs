namespace Fulbr.Core.Models;

/// <summary>
/// A parsed endpoint. <see cref="RawLink"/> is sensitive and must never be logged or
/// persisted in plaintext. The UI layer is responsible for protecting it with DPAPI.
/// </summary>
public sealed record ServerCandidate(
    ProxyProtocol Protocol,
    string Host,
    int Port,
    string DisplayName,
    string RawLink,
    string Fingerprint,
    string? CredentialHash,
    string? Sni,
    string? Transport,
    string? Flow,
    bool TlsEnabled,
    bool InsecureTls,
    IReadOnlyList<string> Warnings)
{
    public string Endpoint => $"{Host}:{Port}";
    public string ProtocolName => Protocol.ToDisplayName();
}
