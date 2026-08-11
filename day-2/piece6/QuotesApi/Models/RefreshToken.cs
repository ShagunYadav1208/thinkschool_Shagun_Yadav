namespace QuotesApi.Models;

public sealed class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
}
