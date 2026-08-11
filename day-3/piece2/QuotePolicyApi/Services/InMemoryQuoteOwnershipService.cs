namespace QuotePolicyApi;

public sealed class InMemoryQuoteOwnershipService : IQuoteOwnershipService
{
    private readonly Dictionary<int, string> _owners = new()
    {
        [1] = "user-123",
        [2] = "user-456"
    };

    public bool IsOwner(int quoteId, string userId) =>
        _owners.TryGetValue(quoteId, out var ownerId)
        && string.Equals(ownerId, userId, StringComparison.Ordinal);
}
