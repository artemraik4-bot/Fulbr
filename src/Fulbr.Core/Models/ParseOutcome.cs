namespace Fulbr.Core.Models;

public sealed record RejectedConfig(string Reason, string RedactedPreview);

public sealed record ParseOutcome(
    IReadOnlyList<ServerCandidate> Servers,
    IReadOnlyList<RejectedConfig> Rejected)
{
    public static ParseOutcome Empty { get; } = new(Array.Empty<ServerCandidate>(), Array.Empty<RejectedConfig>());
}
