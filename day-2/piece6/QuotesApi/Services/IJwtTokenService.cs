using QuotesApi.Models;

namespace QuotesApi.Services;

public interface IJwtTokenService
{
    string CreateAccessToken(User user);
    int AccessTokenLifetimeSeconds { get; }
}
