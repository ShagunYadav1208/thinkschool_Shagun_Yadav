using System.Net;
using ResilientClient.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace ResilientClient.Tests;

public sealed class CircuitBreakerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ProxyQuote_WhenDownstreamKeepsFailing_OpensCircuitAndStopsCallingDownstream()
    {
        var handler = new FakeSequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var factory = new ResilientClientFactory(handler);
        using var client = factory.CreateClient();

        // Call 1: 4 attempts (initial + 3 retries), all fail — retries exhausted normally, 502.
        var first = await client.GetAsync("/api/proxy/quote");

        // Call 2: the 5th cumulative attempt trips MinimumThroughput=5 at FailureRatio=1.0 (>=
        // the 0.5 threshold); the circuit opens mid-call, so the *next* attempt (would-be retry)
        // is rejected before it ever reaches the downstream handler — surfaced as 503, not 502.
        var second = await client.GetAsync("/api/proxy/quote");

        // Call 3: circuit is already open — fails fast without any attempt reaching the handler.
        var third = await client.GetAsync("/api/proxy/quote");

        foreach (var entry in factory.Logs.Entries)
        {
            output.WriteLine($"[{entry.Level}] {entry.Category}: {entry.Message}");
        }

        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);

        // Without the circuit breaker, 3 calls x 4 attempts each = 12 hits on the downstream.
        // With it, the circuit opens partway through call 2 and call 3 never reaches the handler
        // at all — proving calls actually stopped going out, not just that responses changed.
        Assert.Equal(5, handler.CallCount);

        Assert.Contains(
            factory.Logs.Entries,
            e => e.Category == "DownstreamService.Resilience"
                && e.Message.Contains("Circuit breaker OPENED"));
    }
}
