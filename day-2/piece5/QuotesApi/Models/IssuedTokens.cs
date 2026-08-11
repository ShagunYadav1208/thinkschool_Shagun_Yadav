namespace QuotesApi.Models;

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn, DateTimeOffset RefreshTokenExpiresAt);
