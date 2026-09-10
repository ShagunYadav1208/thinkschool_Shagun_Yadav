using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;
using QuotesApi.Resilience;

namespace QuotesApi.Extensions;

public record ConfigureFlakyDependencyRequest(FlakyDependencyMode Mode, int? LatencyMs, int? FailureRatePercent);

public record EnrichResult(string Outcome, QuoteEnrichment? Enrichment, string? Detail);

public static class ResilienceEndpointExtensions
{
    public static void MapResilienceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/resilience");

        group.MapGet("/metrics", (IResilienceMetrics metrics) => Results.Ok(metrics.Snapshot()));

        group.MapPost("/metrics/reset", (IResilienceMetrics metrics) =>
        {
            metrics.Reset();
            return Results.NoContent();
        });

        group.MapGet("/dependency", (FlakyDependencyState state) => Results.Ok(state.Snapshot()));

        group.MapPost("/dependency/configure", (ConfigureFlakyDependencyRequest request, FlakyDependencyState state) =>
        {
            state.Configure(request.Mode, request.LatencyMs, request.FailureRatePercent);
            return Results.Ok(state.Snapshot());
        });

        // The endpoint that actually calls through the Polly pipeline. Classifies the outcome
        // rather than just returning a bare 500/503 for everything, so the demo UI (and this
        // curl transcript in the README) can show exactly which layer of the pipeline handled
        // the failure - the whole point of the exercise's "prove the circuit opens" ask.
        group.MapGet("/enrich/{id:int}", async (
            int id,
            IQuoteEnrichmentClient client,
            IResilienceMetrics metrics,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var enrichment = await client.EnrichAsync(id, cancellationToken);
                return Results.Ok(new EnrichResult("success", enrichment, null));
            }
            catch (BrokenCircuitException ex)
            {
                metrics.RecordCircuitRejection();
                return Results.Ok(new EnrichResult("circuit-open", null, ex.Message));
            }
            catch (RateLimiterRejectedException ex)
            {
                metrics.RecordBulkheadRejection();
                return Results.Ok(new EnrichResult("bulkhead-rejected", null, ex.Message));
            }
            catch (TimeoutRejectedException ex)
            {
                return Results.Ok(new EnrichResult("timeout", null, ex.Message));
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Quote enrichment for {QuoteId} failed after exhausting retries.", id);
                return Results.Ok(new EnrichResult("dependency-failed", null, ex.Message));
            }
        });
    }
}
