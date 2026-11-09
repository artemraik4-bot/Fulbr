using System.Net;
using System.Net.Http.Headers;
using Fulbr.Core.Util;

namespace Fulbr.Core.Subscriptions;

public sealed record FetchResult(
    bool Success,
    bool NotModified,
    int StatusCode,
    string? Content,
    string? Error,
    Uri? FinalUri,
    string? ETag,
    DateTimeOffset? LastModified,
    string? Sha256);

/// <summary>
/// HTTPS-only subscription fetcher with conditional requests, bounded redirects,
/// bounded response size, and no credential forwarding across origins.
/// </summary>
public sealed class SubscriptionFetcher
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private const int MaxRedirects = 3;

    private readonly HttpClient _http;

    public SubscriptionFetcher(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Fulbr/0.1 (open-source; +https://example.invalid/fulbr)");
    }

    public async Task<FetchResult> FetchAsync(
        Uri source,
        string? etag = null,
        DateTimeOffset? lastModified = null,
        CancellationToken cancellationToken = default)
    {
        if (source.Scheme != Uri.UriSchemeHttps)
        {
            return Fail("Only HTTPS subscription URLs are allowed.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));

        var current = SanitizeUri(source, out var userInfo);
        var authorization = BuildAuthorization(userInfo);

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            if (etag is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            if (lastModified is not null)
                request.Headers.IfModifiedSince = lastModified;
            if (authorization is not null)
                request.Headers.Authorization = authorization;

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail("Subscription request timed out.");
            }
            catch (HttpRequestException ex)
            {
                return Fail($"Network error: {Redaction.RedactText(ex.Message)}");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new FetchResult(true, true, 304, null, null, current,
                        response.Headers.ETag?.Tag, response.Content.Headers.LastModified, null);
                }

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location is null)
                        return Fail("Redirect without a Location header.");

                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (next.Scheme != Uri.UriSchemeHttps)
                        return Fail("Redirect to a non-HTTPS URL was rejected.");

                    // Do not forward credentials to a different origin.
                    if (!SameOrigin(current, next))
                        authorization = null;

                    current = SanitizeUri(next, out var nextUserInfo);
                    if (authorization is null && nextUserInfo is not null)
                        authorization = BuildAuthorization(nextUserInfo);

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return Fail($"Subscription returned HTTP {(int)response.StatusCode}.");

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength is > MaxBytes)
                    return Fail("Subscription is larger than the 5 MiB limit.");

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                    .ConfigureAwait(false);
                using var memory = new MemoryStream();
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (memory.Length + read > MaxBytes)
                        return Fail("Subscription exceeded the 5 MiB limit while downloading.");
                    memory.Write(buffer, 0, read);
                }

                var bytes = memory.ToArray();
                var content = System.Text.Encoding.UTF8.GetString(bytes);
                return new FetchResult(true, false, (int)response.StatusCode, content, null,
                    current, response.Headers.ETag?.Tag, response.Content.Headers.LastModified,
                    Hashing.Sha256Hex(bytes));
            }
        }

        return Fail("Too many redirects.");
    }

    private static Uri SanitizeUri(Uri uri, out string? userInfo)
    {
        userInfo = null;
        if (string.IsNullOrEmpty(uri.UserInfo))
            return uri;

        userInfo = Uri.UnescapeDataString(uri.UserInfo);
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        return builder.Uri;
    }

    private static AuthenticationHeaderValue? BuildAuthorization(string? userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
            return null;
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(userInfo));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    private static bool IsRedirect(HttpStatusCode code) => code is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
        HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static FetchResult Fail(string error) =>
        new(false, false, 0, null, error, null, null, null, null);
}
