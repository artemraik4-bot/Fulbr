using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Fulbr.Core.Models;

namespace Fulbr.Core.Diagnostics;

public sealed record PingResult(
    bool Success,
    bool Supported,
    string Method,
    double? LatencyMs,
    string? Error,
    DateTimeOffset CheckedAt)
{
    public static PingResult NotSupported(string reason) =>
        new(false, false, "Not supported", null, reason, DateTimeOffset.UtcNow);

    public static PingResult Failed(string method, string error, double? latency = null) =>
        new(false, true, method, latency, error, DateTimeOffset.UtcNow);
}

/// <summary>
/// Real, bounded endpoint probes. This deliberately separates TCP connect, TLS handshake,
/// and "not testable without the engine" (QUIC/UDP). It never invents a result.
/// </summary>
public sealed class EndpointProbe
{
    public async Task<PingResult> ProbeAsync(ServerCandidate server, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (server.Protocol.UsesUdpTransport())
        {
            return PingResult.NotSupported(
                $"{server.ProtocolName} uses QUIC/UDP; a real handshake test requires the engine.");
        }

        if (server.Protocol.UsesTls() && server.TlsEnabled)
            return await TlsHandshakeAsync(server, timeout, cancellationToken).ConfigureAwait(false);

        return await TcpConnectAsync(server, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PingResult> TcpConnectAsync(ServerCandidate server, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            await client.ConnectAsync(server.Host, server.Port, linked.Token).ConfigureAwait(false);
            sw.Stop();
            return new PingResult(true, true, "TCP connect", sw.Elapsed.TotalMilliseconds,
                null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return PingResult.Failed("TCP connect", "Timed out.", sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return PingResult.Failed("TCP connect", $"Socket error: {ex.SocketErrorCode}.",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return PingResult.Failed("TCP connect", $"Connection failed: {ex.GetType().Name}.",
                sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<PingResult> TlsHandshakeAsync(ServerCandidate server, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            await client.ConnectAsync(server.Host, server.Port, linked.Token).ConfigureAwait(false);

            await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            var options = new SslClientAuthenticationOptions
            {
                TargetHost = string.IsNullOrWhiteSpace(server.Sni) ? server.Host : server.Sni,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode =
                    System.Security.Cryptography.X509Certificates.X509RevocationMode.Online
            };

            await ssl.AuthenticateAsClientAsync(options, linked.Token).ConfigureAwait(false);
            sw.Stop();
            return new PingResult(true, true, "TLS handshake", sw.Elapsed.TotalMilliseconds,
                null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return PingResult.Failed("TLS handshake", "Timed out.", sw.Elapsed.TotalMilliseconds);
        }
        catch (AuthenticationException ex)
        {
            sw.Stop();
            return PingResult.Failed("TLS handshake",
                $"TLS validation failed: {ex.Message}", sw.Elapsed.TotalMilliseconds);
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return PingResult.Failed("TLS handshake", $"Socket error: {ex.SocketErrorCode}.",
                sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return PingResult.Failed("TLS handshake", $"Handshake failed: {ex.GetType().Name}.",
                sw.Elapsed.TotalMilliseconds);
        }
    }
}
