using Fulbr.Core.Models;

namespace Fulbr.Core.Ranking;

public enum RankingPreset
{
    Balanced,
    LowestLatency,
    HighestStability,
    HighestSpeed
}

public sealed record RankedServer(
    ServerRecord Server,
    double Score,
    double SuccessComponent,
    double LatencyComponent,
    double StabilityComponent,
    double FreshnessComponent,
    double FailurePenalty);

/// <summary>
/// Deterministic, explainable ranking. A server with no measurements cannot outrank a
/// measured healthy server. Wilson lower bound prevents 1/1 = 100% from winning.
/// </summary>
public sealed class ServerRanker
{
    public IReadOnlyList<RankedServer> Rank(IEnumerable<ServerRecord> servers, RankingPreset preset)
    {
        var result = servers.Select(s => RankOne(s, preset))
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Server.MedianLatencyMs ?? double.MaxValue)
            .ThenBy(r => r.Server.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return result;
    }

    public RankedServer RankOne(ServerRecord server, RankingPreset preset)
    {
        var total = server.SuccessCount + server.FailureCount;

        // A server with no measurements cannot outrank a measured healthy server.
        if (total == 0)
            return new RankedServer(server, 0, 0, 0, 0, 0, 0);

        var success = WilsonLowerBound(server.SuccessCount, total, 1.96);
        var latencyMs = server.MedianLatencyMs ?? server.LastLatencyMs;
        var latency = latencyMs is null ? 0 : 1.0 - Math.Clamp(latencyMs.Value / 1000.0, 0, 1);
        var stability = server.JitterMs is null ? 0.5 : 1.0 - Math.Clamp(server.JitterMs.Value / 500.0, 0, 1);
        var hours = server.LastCheckedAt is null
            ? 999
            : Math.Max(0, (DateTimeOffset.UtcNow - server.LastCheckedAt.Value).TotalHours);
        var freshness = Math.Exp(-hours / 72.0);
        var failurePenalty = Math.Min(1.0, server.FailureCount / 5.0);

        var (wSuccess, wLatency, wStability, wFreshness) = preset switch
        {
            RankingPreset.LowestLatency => (0.25, 0.45, 0.15, 0.15),
            RankingPreset.HighestStability => (0.35, 0.15, 0.35, 0.15),
            RankingPreset.HighestSpeed => (0.25, 0.20, 0.15, 0.40),
            _ => (0.35, 0.25, 0.20, 0.20)
        };

        var score = 100.0 * (
            wSuccess * success +
            wLatency * latency +
            wStability * stability +
            wFreshness * freshness -
            0.30 * failurePenalty);

        score = Math.Clamp(score, 0, 100);
        return new RankedServer(server, score, success, latency, stability, freshness, failurePenalty);
    }

    public static double WilsonLowerBound(int successes, int total, double z)
    {
        if (total <= 0)
            return 0;
        var p = (double)successes / total;
        var denominator = 1 + z * z / total;
        var centre = p + z * z / (2.0 * total);
        var margin = z * Math.Sqrt((p * (1 - p) + z * z / (4.0 * total)) / total);
        return Math.Max(0, (centre - margin) / denominator);
    }
}
