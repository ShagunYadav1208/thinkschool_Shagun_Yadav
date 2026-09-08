using System.Net.Http.Json;

namespace QuotesApi.Resilience;

public record QuoteEnrichment(int QuoteId, string Enrichment);

/// <summary>The outbound dependency this exercise wraps with Polly - see
/// InfrastructureExtensions.cs for the resilience pipeline and FlakyDependencyHandler for what's
/// actually behind the HttpClient.</summary>
public interface IQuoteEnrichmentClient
{
    Task<QuoteEnrichment> EnrichAsync(int quoteId, CancellationToken cancellationToken);
}

public sealed class QuoteEnrichmentClient(HttpClient httpClient) : IQuoteEnrichmentClient
{
    public async Task<QuoteEnrichment> EnrichAsync(int quoteId, CancellationToken cancellationToken)
    {
        // GET only, deliberately - this client issues no writes, which is what makes "retry is
        // idempotent-only" true by construction rather than by a predicate that has to be trusted
        // to get every future call site right.
        using var response = await httpClient.GetAsync($"/enrich/{quoteId}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<QuoteEnrichment>(cancellationToken);
        return payload ?? throw new InvalidOperationException("Enrichment dependency returned an empty payload.");
    }
}
