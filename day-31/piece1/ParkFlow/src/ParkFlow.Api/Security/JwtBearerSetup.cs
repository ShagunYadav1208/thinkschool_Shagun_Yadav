using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ParkFlow.Api.Security;

/// <summary>
/// Configuration keys and defaults for the JWT bearer scheme added in ADR-001 (day-28/piece1).
/// <c>Issuer</c>/<c>Audience</c> are fixed constants (not secrets — they're just identifiers), only
/// <c>SigningKey</c> comes from configuration, mirroring how <see cref="ApiKeyAuthenticationOptions"/>
/// treats <c>Security:ApiKey</c>.
/// </summary>
public static class JwtBearerSetup
{
    public const string Issuer = "ParkFlow.Dev";
    public const string Audience = "ParkFlow.Api";
    public const string SigningKeyConfigPath = "Security:Jwt:SigningKey";

    public static TokenValidationParameters BuildValidationParameters(string signingKey) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };

    public static void Configure(JwtBearerOptions options, string signingKey)
    {
        // Keep "sub" mapped to ClaimTypes.NameIdentifier (JwtSecurityTokenHandler's classic inbound
        // claim mapping) rather than leaving it as the raw "sub" claim type — Build Day 30's
        // ownership check (BUILD-PLAN.md) reads User.FindFirstValue(ClaimTypes.NameIdentifier), the
        // same claim the rest of ASP.NET Core identity code already expects.
        options.MapInboundClaims = true;
        options.TokenValidationParameters = BuildValidationParameters(signingKey);

        // JwtSecurityTokenHandler.ValidateToken tags the identity it returns with
        // AuthenticationType "AuthenticationTypes.Federation", not the scheme name — so re-tag it
        // as "Bearer" here. RequireApiKeyAndBearerHandler needs to tell "authenticated via the
        // bearer scheme" apart from "authenticated via the API key scheme" by that name.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity { IsAuthenticated: true } identity)
                {
                    context.Principal = new ClaimsPrincipal(
                        new ClaimsIdentity(identity.Claims, JwtBearerDefaults.AuthenticationScheme));
                }

                return Task.CompletedTask;
            },
        };
    }
}
