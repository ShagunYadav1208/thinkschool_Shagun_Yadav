using System.Text;

namespace QuotesLockedApi;

public sealed record User(string Id, string Email, string Password, string[] Scopes);

public sealed record JwtOptions(string Issuer, string Audience, string Key)
{
    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(Key) < 32)
        {
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
        }
    }
}

public sealed record EntraOptions(string TenantId, string ClientId, string Audience);
