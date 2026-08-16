namespace Quotes.Domain;

public interface ITokenService
{
    int AccessTokenLifetimeSeconds { get; }

    string CreateAccessToken(User user, string[] scopes);

    string CreateRefreshToken();

    string HashRefreshToken(string rawRefreshToken);
}
