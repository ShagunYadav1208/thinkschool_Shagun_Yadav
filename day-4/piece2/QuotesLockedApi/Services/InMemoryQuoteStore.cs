namespace QuotesLockedApi;

public sealed class InMemoryQuoteStore : IQuoteStore
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, Quote> _quotes = new()
    {
        [1] = new Quote(1, "First seeded quote", "user-123"),
        [2] = new Quote(2, "Reader owned quote", "user-456")
    };
    private int _nextId = 3;

    public IReadOnlyCollection<Quote> All()
    {
        lock (_lock)
        {
            return _quotes.Values.ToArray();
        }
    }

    public Quote Create(string text, string ownerId)
    {
        lock (_lock)
        {
            var quote = new Quote(_nextId++, text, ownerId);
            _quotes[quote.Id] = quote;
            return quote;
        }
    }

    public Quote? Update(int id, string text)
    {
        lock (_lock)
        {
            if (!_quotes.TryGetValue(id, out var existing))
            {
                return null;
            }

            var updated = existing with { Text = text };
            _quotes[id] = updated;
            return updated;
        }
    }

    public bool Delete(int id)
    {
        lock (_lock)
        {
            return _quotes.Remove(id);
        }
    }

    public bool IsOwner(int id, string userId)
    {
        lock (_lock)
        {
            return _quotes.TryGetValue(id, out var quote)
                && string.Equals(quote.OwnerId, userId, StringComparison.Ordinal);
        }
    }
}
