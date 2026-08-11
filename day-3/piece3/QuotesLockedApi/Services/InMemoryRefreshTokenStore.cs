namespace QuotesLockedApi;

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly Lock _lock = new();
    private readonly List<RefreshTokenRecord> _tokens = [];

    public void Add(RefreshTokenRecord token)
    {
        lock (_lock)
        {
            _tokens.Add(token);
        }
    }

    public RefreshTokenRecord? FindByHash(string hash)
    {
        lock (_lock)
        {
            return _tokens.SingleOrDefault(token => token.TokenHash == hash);
        }
    }

    public IReadOnlyCollection<RefreshTokenRecord> ActiveFamilyTokens(Guid familyId)
    {
        lock (_lock)
        {
            return _tokens
                .Where(token => token.FamilyId == familyId && token.RevokedAt is null)
                .ToArray();
        }
    }
}
