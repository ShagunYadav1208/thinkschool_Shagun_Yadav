namespace QuotesApi.Models;

public sealed record RefreshResult(TokenResponse? Tokens, bool ReuseDetected)
{
    public bool IsSuccess => Tokens is not null;
}
