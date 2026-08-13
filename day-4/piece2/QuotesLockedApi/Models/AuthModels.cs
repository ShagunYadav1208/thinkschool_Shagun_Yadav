namespace QuotesLockedApi;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record InternalTokenRequest(
    string Subject,
    string ClientSecret,
    string[]? Scopes,
    int? ExpiresInSeconds);

public sealed record AccessTokenResponse(string AccessToken, int ExpiresIn);

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record RefreshResult(TokenResponse? Tokens, bool ReuseDetected);
