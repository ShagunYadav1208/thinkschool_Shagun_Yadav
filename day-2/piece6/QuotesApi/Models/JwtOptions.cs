using System.Text;

namespace QuotesApi.Models;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; }
    public int RefreshTokenDays { get; init; }

    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(Key) < 32 || AccessTokenMinutes <= 0 || RefreshTokenDays <= 0)
            throw new InvalidOperationException("JWT configuration is invalid.");
    }
}
