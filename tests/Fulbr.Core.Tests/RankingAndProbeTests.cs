using Fulbr.Core.Diagnostics;
using Fulbr.Core.Models;
using Fulbr.Core.Ranking;
using Xunit;

namespace Fulbr.Core.Tests;

public sealed class RankingAndProbeTests
{
    private static ServerRecord Server(string name, int success, int failure, double? median, double? jitter)
        => new()
        {
            Id = name,
            Protocol = ProxyProtocol.Vless,
            Host = "example.com",
            Port = 443,
            DisplayName = name,
            Fingerprint = name,
            SuccessCount = success,
            FailureCount = failure,
            MedianLatencyMs = median,
            LastLatencyMs = median,
            JitterMs = jitter,
            LastCheckedAt = DateTimeOffset.UtcNow
        };

    [Fact]
    public void StableServerOutranksFragileLowPingServer()
    {
        var stable = Server("stable", 20, 0, 120, 10);
        var fragile = Server("fragile", 5, 5, 40, 300);
        var ranked = new ServerRanker().Rank(new[] { stable, fragile }, RankingPreset.Balanced);

        Assert.Equal("stable", ranked[0].Server.DisplayName);
    }

    [Fact]
    public void UnmeasuredServerScoresZero()
    {
        var ranked = new ServerRanker().RankOne(Server("new", 0, 0, null, null), RankingPreset.Balanced);
        Assert.Equal(0, ranked.Score);
    }

    [Fact]
    public async Task TcpProbeConnectsToLocalListener()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var accept = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            client.Close();
        });

        var server = new ServerCandidate(
            ProxyProtocol.Shadowsocks, "127.0.0.1", port, "local", "ss://x", "fp",
            null, null, null, null, false, false, Array.Empty<string>());

        var result = await new EndpointProbe().TcpConnectAsync(server, TimeSpan.FromSeconds(3));
        listener.Stop();
        await accept;

        Assert.True(result.Success);
        Assert.Equal("TCP connect", result.Method);
        Assert.NotNull(result.LatencyMs);
    }

    [Fact]
    public async Task UdpProtocolIsNotFakedAsTcpPing()
    {
        var server = new ServerCandidate(
            ProxyProtocol.Hysteria2, "example.com", 443, "hy", "hysteria2://x", "fp",
            null, "example.com", null, null, true, false, Array.Empty<string>());

        var result = await new EndpointProbe().ProbeAsync(server, TimeSpan.FromSeconds(1));
        Assert.False(result.Supported);
        Assert.Contains("engine", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
