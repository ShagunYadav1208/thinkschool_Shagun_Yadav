namespace Quotes.Domain;

public sealed class RefreshTokenRecord
{
    public required string TokenHash { get; init; }
    public required string UserId { get; init; }
    public required Guid FamilyId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
