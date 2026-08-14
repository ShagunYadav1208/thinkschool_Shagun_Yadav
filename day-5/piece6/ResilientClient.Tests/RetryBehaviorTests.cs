using System.Net;
using ResilientClient.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace ResilientClient.Tests;

public sealed class RetryBehaviorTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProxyQuote_WhenDownstreamFailsTwiceThenSucceeds_RetriesAndReturnsSuccess()
    {
        var handler = new FakeSequenceHandler(callNumber => callNumber < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"quote":"Fortune favors the retried."}""")
            });

        using var factory = new ResilientClientFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/proxy/quote");

        foreach (var entry in factory.Logs.Entries)
        {
            output.WriteLine($"[{entry.Level}] {entry.Category}: {entry.Message}");
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.CallCount); // initial attempt + 2 retries before success

        var retryLogs = factory.Logs.Entries
            .Where(e => e.Category == "DownstreamService.Resilience" && e.Message.StartsWith("Retry"))
            .Select(e => e.Message)
            .ToList();

        Assert.Equal(2, retryLogs.Count);
        Assert.Contains(retryLogs, m => m.StartsWith("Retry 1 of 3"));
        Assert.Contains(retryLogs, m => m.StartsWith("Retry 2 of 3"));
    }

    [Fact]
    public async Task ProxyQuote_WhenDownstreamAlwaysFails_ExhaustsAllRetriesAndReturnsBadGateway()
    {
        var handler = new FakeSequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var factory = new ResilientClientFactory(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/proxy/quote");

        foreach (var entry in factory.Logs.Entries)
        {
            output.WriteLine($"[{entry.Level}] {entry.Category}: {entry.Message}");
        }

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(4, handler.CallCount); // initial attempt + all 3 retries, then gives up

        var retryLogCount = factory.Logs.Entries
            .Count(e => e.Category == "DownstreamService.Resilience" && e.Message.StartsWith("Retry"));
        Assert.Equal(3, retryLogCount);
    }
}
