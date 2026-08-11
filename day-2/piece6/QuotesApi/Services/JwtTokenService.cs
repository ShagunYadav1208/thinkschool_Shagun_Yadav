using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace QuotesApi.Services;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions jwt = options.Value;
    public int AccessTokenLifetimeSeconds => jwt.AccessTokenMinutes * 60;

    public string CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, user.Email)],
            notBefore: now,
            expires: now.AddMinutes(jwt.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
