using System.Net;
using System.Text;

namespace QuotesApi.Resilience;

/// <summary>
/// The primary HttpMessageHandler for the "quote enrichment" HttpClient - stands in for the
/// socket/TLS/DNS work a real outbound call would do. Registered via
/// ConfigurePrimaryHttpMessageHandler in InfrastructureExtensions.cs, so every
/// IQuoteEnrichmentClient.EnrichAsync call still flows through a real HttpRequestMessage and the
/// full Polly resilience pipeline exactly as it would for any other typed HttpClient - only the
/// bottom-most "make the network call" step is faked, deterministically, from FlakyDependencyState.
/// That's what makes the circuit-breaker demo reproducible on demand instead of waiting on some
/// real external service to actually be down.
/// </summary>
public sealed class FlakyDependencyHandler(FlakyDependencyState state, IResilienceMetrics metrics) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        metrics.RecordDependencyAttempt();

        var (shouldFail, delayMs) = state.NextOutcome();
        await Task.Delay(delayMs, cancellationToken);

        if (shouldFail)
        {
            metrics.RecordDependencyFailure();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                Content = new StringContent("""{"error":"simulated dependency failure"}""", Encoding.UTF8, "application/json"),
            };
        }

        metrics.RecordDependencySuccess();
        var quoteId = request.RequestUri?.Segments[^1] ?? "0";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(
                $$"""{"quoteId":{{quoteId}},"enrichment":"sentiment: reflective, era: unknown"}""",
                Encoding.UTF8,
                "application/json"),
        };
    }
}
