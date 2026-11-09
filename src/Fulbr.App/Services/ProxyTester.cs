using System.Net;
using System.Net.Http;

namespace Fulbr.App.Services;

/// <summary>
/// Performs a real HTTP request through the local proxy. A successful result proves the
/// engine started, the inbound accepted authentication, and the selected outbound carried
/// traffic. It does not prove that the exit server is trustworthy.
/// </summary>
public static class ProxyTester
{
    private static readonly string[] Targets =
    {
        "https://cp.cloudflare.com/generate_204",
        "https://www.gstatic.com/generate_204"
    };

    public static async Task<(bool Ok, string Detail)> TestAsync(
        EngineSession session, CancellationToken cancellationToken = default)
    {
        using var handler = new HttpClientHandler
        {
            UseProxy = true,
            PreAuthenticate = true,
            AllowAutoRedirect = false,
            Proxy = new WebProxy(session.ProxyUri)
            {
                Credentials = new NetworkCredential(session.Username, session.Password)
            }
        };

        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Fulbr/0.1");

        var errors = new List<string>();
        foreach (var target in Targets)
        {
            try
            {
                using var response = await http.GetAsync(target, cancellationToken);
                if ((int)response.StatusCode is 200 or 204)
                    return (true, $"HTTP {(int)response.StatusCode} through the local proxy.");
                errors.Add($"{new Uri(target).Host}: HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{new Uri(target).Host}: timed out");
            }
            catch (Exception ex)
            {
                errors.Add($"{new Uri(target).Host}: {ex.GetType().Name}");
            }
        }

        return (false, "The proxy did not pass an HTTP check: " + string.Join("; ", errors));
    }
}
