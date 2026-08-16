namespace Quotes.Domain;

public interface IRefreshTokenService
{
    TokenResponse Issue(User user);

    RefreshResult Refresh(string rawRefreshToken);
}
