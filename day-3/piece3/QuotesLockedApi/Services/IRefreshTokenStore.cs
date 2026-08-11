namespace QuotesLockedApi;

public interface IRefreshTokenStore
{
    void Add(RefreshTokenRecord token);
    RefreshTokenRecord? FindByHash(string hash);
    IReadOnlyCollection<RefreshTokenRecord> ActiveFamilyTokens(Guid familyId);
}
