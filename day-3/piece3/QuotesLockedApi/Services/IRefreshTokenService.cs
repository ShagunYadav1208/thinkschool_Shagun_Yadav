namespace QuotesLockedApi;

public interface IRefreshTokenService
{
    TokenResponse Issue(User user);
    RefreshResult Refresh(string refreshToken);
}
