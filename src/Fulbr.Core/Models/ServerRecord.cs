namespace Fulbr.Core.Models;

/// <summary>
/// A server as shown in the UI. Raw configuration is stored separately in protected form.
/// </summary>
public sealed class ServerRecord
{
    public required string Id { get; init; }
    public required ProxyProtocol Protocol { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string DisplayName { get; set; }
    public required string Fingerprint { get; init; }
    public string? SourceId { get; init; }
    public string? SourceName { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsUserCreated { get; init; }
    public bool InsecureTls { get; init; }
    public bool TlsEnabled { get; init; }
    public string? Sni { get; init; }
    public string? Transport { get; init; }
    public string? Flow { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string Trust { get; set; } = "Unknown";

    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double? LastLatencyMs { get; set; }
    public double? MedianLatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public string? LastProbeMethod { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastError { get; set; }

    public string Endpoint => $"{Host}:{Port}";
    public string ProtocolName => Protocol.ToDisplayName();
    public string Status => LastCheckedAt is null
        ? "Not tested"
        : LastError is not null ? "Unavailable" : "Reachable";
}
