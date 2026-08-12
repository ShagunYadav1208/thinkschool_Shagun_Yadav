namespace Quotes.Domain;

public sealed class QuoteExpiryService(IClock clock)
{
    public TimeSpan GetAge(Quote quote)
    {
        var age = clock.UtcNow - quote.CreatedAt;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }

    public bool IsExpired(Quote quote, TimeSpan ttl) =>
        ttl <= TimeSpan.Zero || GetAge(quote) >= ttl;
}
