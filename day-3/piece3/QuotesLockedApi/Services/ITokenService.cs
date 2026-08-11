namespace QuotesLockedApi;

public interface ITokenService
{
    int AccessTokenLifetimeSeconds { get; }
    string CreateAccessToken(User user, string[] scopes, TimeSpan? lifetime = null);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
}
