namespace Quotes.Domain;

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record RefreshResult(TokenResponse? Tokens, bool ReuseDetected);
