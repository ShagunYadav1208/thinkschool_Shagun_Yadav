namespace Quotes.Domain;

public sealed record TokenPair(string AccessToken, string RefreshToken);

public sealed record RefreshResult(TokenPair? Tokens, bool ReuseDetected)
{
    public bool IsSuccess => Tokens is not null;
}

internal sealed class RefreshTokenRecord
{
    public required string UserId { get; init; }
    public required Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
