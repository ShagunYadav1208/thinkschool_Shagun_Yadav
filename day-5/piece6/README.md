# Day 5 - Add Polly resilience to HTTP calls

A small standalone API (`ResilientClient`) with one named `HttpClient` wired through
`Microsoft.Extensions.Http.Resilience`, and tests that force real transient failures through the
real pipeline — no mocking framework, no real network, no Azure dependency. Everything in this
piece actually builds, runs, and passes.

## `HttpClient` + resilience handler config

From [`ResilientClient/Program.cs`](ResilientClient/Program.cs):

```csharp
builder.Services.AddHttpClient("downstream-service", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["DownstreamService:BaseUrl"] ?? "https://downstream.invalid/");

    // The resilience pipeline below owns the timeout budget (AddTimeout). Leaving HttpClient's
    // own Timeout at its default (100s) means it never fires first and silently pre-empts what
    // AddTimeout is supposed to control.
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.AddResilienceHandler("default", (pipeline, context) =>
{
    var logger = context.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DownstreamService.Resilience");

    // Strategies wrap each other in the order they're added — the first one added is outermost.
    // AddTimeout goes first here deliberately, so it's a *total* budget across every retry
    // combined. Adding it last instead would make it a per-attempt timeout (each retry getting
    // its own fresh 10s), which silently changes "total timeout 10 seconds" into something that
    // could take 40+ seconds end to end across 4 attempts.
    pipeline.AddTimeout(TimeSpan.FromSeconds(10));

    // 3 retries, exponential backoff, jittered — jitter matters here: without it, every failed
    // instance of this service retries in lockstep, turning a downstream blip into a synchronized
    // retry storm instead of smoothing it out.
    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromSeconds(1),
        OnRetry = args =>
        {
            logger.LogWarning(
                "Retry {AttemptNumber} of {MaxRetryAttempts} for {RequestUri} after {DelayMs}ms — {Outcome}",
                args.AttemptNumber + 1, 3,
                args.Outcome.Result?.RequestMessage?.RequestUri,
                args.RetryDelay.TotalMilliseconds,
                DescribeOutcome(args.Outcome));
            return default;
        }
    });

    // Circuit opens once >= 50% of calls fail within a 30s sampling window (given at least 5
    // calls sampled, so one unlucky request early on can't trip it alone) and stays open 30s
    // before allowing a single trial call through.
    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30),
        OnOpened = args =>
        {
            logger.LogError(
                "Circuit breaker OPENED for downstream-service — will stop calling out for {BreakDuration}",
                args.BreakDuration);
            return default;
        },
        OnClosed = _ => { logger.LogInformation("Circuit breaker CLOSED for downstream-service — calls resuming"); return default; },
        OnHalfOpened = _ => { logger.LogInformation("Circuit breaker HALF-OPEN for downstream-service — trial call in flight"); return default; }
    });
});
```

The endpoint itself (`GET /api/proxy/quote`) never silently swallows a failure — every terminal
outcome is logged and mapped to a distinct, meaningful status code:

```csharp
catch (BrokenCircuitException ex)
{
    logger.LogError(ex, "downstream-service circuit is open; failing fast instead of calling out");
    return Results.Problem("Downstream service is currently unavailable.", statusCode: 503);
}
catch (TimeoutRejectedException ex)
{
    logger.LogError(ex, "downstream-service call exceeded its resilience timeout budget");
    return Results.Problem("Downstream service timed out.", statusCode: 504);
}
catch (HttpRequestException ex)
{
    logger.LogError(ex, "downstream-service call failed after retries were exhausted");
    return Results.Problem("Downstream service call failed.", statusCode: 502);
}
```

## The test — forcing a transient failure through the real pipeline

[`ResilientClient.Tests/TestSupport/FakeSequenceHandler.cs`](ResilientClient.Tests/TestSupport/FakeSequenceHandler.cs)
replaces only the innermost `HttpMessageHandler` (no real socket, no real network) so the actual
`AddResilienceHandler` pipeline from `Program.cs` runs completely unmodified around it:

```csharp
public sealed class FakeSequenceHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _callCount;
    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        var response = respond(callNumber);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
```

[`RetryBehaviorTests.cs`](ResilientClient.Tests/RetryBehaviorTests.cs) forces exactly two transient
`503`s before succeeding:

```csharp
var handler = new FakeSequenceHandler(callNumber => callNumber < 3
    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"quote":"..."}""") });

var response = await client.GetAsync("/api/proxy/quote");

Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal(3, handler.CallCount); // initial attempt + 2 retries before success
```

### Real retry logs from a passing test run

```
dotnet test Day5Piece6.slnx --logger "console;verbosity=detailed"
...
 [Warning] DownstreamService.Resilience: Retry 1 of 3 for https://downstream.invalid/quote after 591.4334ms — HTTP 503
 [Warning] DownstreamService.Resilience: Retry 2 of 3 for https://downstream.invalid/quote after 1041.3039ms — HTTP 503
 [Information] ... Received HTTP response headers after 0.1853ms - 200
 [Information] Microsoft.AspNetCore.Hosting.Diagnostics: Request finished ... - 200 39 application/json 1681.3492ms

  Passed ResilientClient.Tests.RetryBehaviorTests.ProxyQuote_WhenDownstreamFailsTwiceThenSucceeds_RetriesAndReturnsSuccess [1 s]
```

Note the delays growing (~590ms, ~1040ms) — that's the exponential backoff with jitter actually
firing, not a fixed interval. A second test in the same file
(`ProxyQuote_WhenDownstreamAlwaysFails_ExhaustsAllRetriesAndReturnsBadGateway`) forces the
downstream to fail every time and asserts exactly 3 retry logs, 4 total attempts, and a final `502`
— retries genuinely exhausted, not silently swallowed.

### The circuit breaker test — same real pipeline, three calls

[`CircuitBreakerTests.cs`](ResilientClient.Tests/CircuitBreakerTests.cs) makes 3 sequential calls
against an always-failing downstream and captures exactly what actually happened:

```
 [Error] DownstreamService.Resilience: Circuit breaker OPENED for downstream-service — will stop calling out for 00:00:30
 [Information] Polly: ... Result: 'The circuit is now open and is not allowing calls.', Handled: 'False' ...
 [Error] Program: downstream-service circuit is open; failing fast instead of calling out

  Passed ResilientClient.Tests.CircuitBreakerTests.ProxyQuote_WhenDownstreamKeepsFailing_OpensCircuitAndStopsCallingDownstream [6 s]
```

Call 1 uses all 4 attempts (initial + 3 retries) and returns `502` — retries genuinely exhausted.
That's the 4th, 5th cumulative attempt through the circuit breaker; `MinimumThroughput: 5` is now
satisfied at `FailureRatio: 1.0`, so the circuit opens mid-way through call 2's first retry — its
*next* retry attempt is rejected before ever reaching the downstream handler, surfacing as `503`
instead of `502`. Call 3 fails immediately, no attempt reaching the handler at all. Total hits on
the fake downstream across all 3 calls: **5**, not the 12 it would take without a circuit breaker —
proving calls actually stopped going out, not just that the response changed:

```csharp
Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
Assert.Equal(5, handler.CallCount);
```

All 3 tests pass:

```
Test Run Successful.
Total tests: 3
     Passed: 3
```

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/main/day-5/piece6

(Not yet pushed — I don't commit or push without being asked. Ready for you to review, stage, and
push yourself.)

## Notes for mentor

The exercise's own example ("Entra ID for token validation") is already live in this repo — [Day 3
Piece 1](../../day-3/piece1)'s `AddJwtBearer` with `options.Authority =
https://login.microsoftonline.com/{tenantId}/v2.0` makes its own outbound HTTP calls (OIDC
discovery + JWKS fetch) via ASP.NET Core's built-in `JwtBearerHandler.Backchannel`. This piece
builds a fresh, standalone client instead of retrofitting that one, because testing it here would
mean faking Entra's real discovery document shape rather than a clean HTTP call — but the same
`AddResilienceHandler` technique applies directly to `options.Backchannel` if that client ever
needed hardening.

## What did I learn this session?

Strategy order in `AddResilienceHandler` isn't cosmetic — the first strategy added is outermost,
and getting `AddTimeout` after `AddRetry` (instead of before it) silently turns a 10-second *total*
budget into a 10-second *per-attempt* one, which across 4 attempts is a 40-second worst case
instead of 10. I caught this only because I stopped to reason through what "total timeout 10
seconds" actually requires before writing the config, not after.

## What would break this?

Retry and circuit breaker interact in a way that's easy to get wrong when reasoning about how many
times a "flaky" downstream actually gets hit: because `AddRetry` wraps `AddCircuitBreaker`, every
individual retry attempt — not just every top-level call — counts against the circuit breaker's
`MinimumThroughput`/`FailureRatio` window. A downstream that's merely a little flaky (say, 20%
transient failures) can still trip a `FailureRatio: 0.5` breaker faster than expected, because each
logical call that needs even one retry contributes multiple failed attempts to the same rolling
window, not one.
