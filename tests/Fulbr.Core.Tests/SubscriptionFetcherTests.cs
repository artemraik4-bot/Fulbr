using Fulbr.Core.Subscriptions;
using Xunit;

namespace Fulbr.Core.Tests;

public sealed class SubscriptionFetcherTests
{
    [Fact]
    public async Task RejectsPlainHttp()
    {
        var fetcher = new SubscriptionFetcher();
        var result = await fetcher.FetchAsync(new Uri("http://example.com/sub.txt"));
        Assert.False(result.Success);
        Assert.Contains("HTTPS", result.Error);
    }
}
