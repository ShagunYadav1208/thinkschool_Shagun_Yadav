using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();

// Named client for whatever downstream this API depends on (Entra ID token validation, another
// internal service, a third-party API, ...). BaseAddress is config-driven so tests can point it
// at a fake in-process handler instead of a real network address.
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
                args.AttemptNumber + 1,
                3,
                args.Outcome.Result?.RequestMessage?.RequestUri,
                args.RetryDelay.TotalMilliseconds,
                DescribeOutcome(args.Outcome));
            return default;
        }
    });

    // Circuit opens once >= 50% of calls fail within a 30s sampling window, with at least 5
    // sampled executions before the ratio is judged. IMPORTANT, found via smoke test (see Day 5
    // Piece 7): "executions" here counts every retry attempt, not every top-level call — a single
    // logical call that exhausts all 3 retries already contributes 4 failed executions toward
    // MinimumThroughput. In practice this opened the circuit off of ONE unlucky logical call in
    // testing, not several — the "one bad request can't trip it alone" assumption this comment
    // originally made was wrong. If that isolation actually matters, MinimumThroughput needs to
    // account for MaxRetryAttempts + 1, not be sized as if retries didn't count.
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
        OnClosed = _ =>
        {
            logger.LogInformation("Circuit breaker CLOSED for downstream-service — calls resuming");
            return default;
        },
        OnHalfOpened = _ =>
        {
            logger.LogInformation("Circuit breaker HALF-OPEN for downstream-service — trial call in flight");
            return default;
        }
    });
});

var app = builder.Build();

app.MapGet("/api/proxy/quote", async (IHttpClientFactory httpClientFactory, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    var client = httpClientFactory.CreateClient("downstream-service");

    try
    {
        var response = await client.GetAsync("/quote", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(body, "application/json");
    }
    catch (BrokenCircuitException ex)
    {
        // Never silently swallow: the circuit being open is itself a failure worth surfacing,
        // not a reason to return a fake 200.
        logger.LogError(ex, "downstream-service circuit is open; failing fast instead of calling out");
        return Results.Problem("Downstream service is currently unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TimeoutRejectedException ex)
    {
        logger.LogError(ex, "downstream-service call exceeded its resilience timeout budget");
        return Results.Problem("Downstream service timed out.", statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "downstream-service call failed after retries were exhausted");
        return Results.Problem("Downstream service call failed.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static string DescribeOutcome(Outcome<HttpResponseMessage> outcome) =>
    outcome.Exception is not null
        ? outcome.Exception.GetType().Name
        : $"HTTP {(int)outcome.Result!.StatusCode}";

public partial class Program;
