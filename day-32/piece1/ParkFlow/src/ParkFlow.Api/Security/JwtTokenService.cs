using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ParkFlow.Api.Security;

/// <summary>
/// Issues the same shape of bearer token the old Development-only /api/v1/dev/token endpoint used
/// to mint for a hardcoded demo user (ADR-001) — now issued by real /api/v1/auth/register and
/// /api/v1/auth/login instead, for a real account backed by the Identity module. "sub" stays the
/// claim every ownership check downstream already reads via ClaimTypes.NameIdentifier
/// (see JwtBearerSetup.Configure).
/// </summary>
public sealed class JwtTokenService(IConfiguration configuration)
{
    public (string Token, DateTime ExpiresAt) IssueToken(Guid userId)
    {
        var signingKey = configuration[JwtBearerSetup.SigningKeyConfigPath] ?? string.Empty;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var token = new JwtSecurityToken(
            issuer: JwtBearerSetup.Issuer,
            audience: JwtBearerSetup.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
