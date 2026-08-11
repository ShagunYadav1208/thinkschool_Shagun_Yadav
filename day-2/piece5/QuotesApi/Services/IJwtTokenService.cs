using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IJwtTokenService
{
    IssuedTokens Issue(User user);
    string HashRefreshToken(string refreshToken);
}
