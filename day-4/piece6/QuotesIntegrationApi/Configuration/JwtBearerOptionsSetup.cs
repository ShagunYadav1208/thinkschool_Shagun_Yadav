using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace QuotesIntegrationApi.Configuration;

/// <summary>
/// This is "inject IOptions&lt;JwtOptions&gt; where you need it" for the one consumer that isn't a
/// request-handling service: the JWT bearer handler's own options object. <see cref="JwtOptions"/>
/// is only read once, when the framework builds <see cref="JwtBearerOptions"/> for the scheme, so
/// <see cref="IOptions{TOptions}"/> (not <see cref="IOptionsMonitor{TOptions}"/>) is the right
/// choice here — this class isn't a long-lived singleton re-reading it on every request.
/// </summary>
public sealed class JwtBearerOptionsSetup(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options) => Configure(options);

    public void Configure(JwtBearerOptions options)
    {
        var jwt = jwtOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.NameIdentifier
        };
    }
}
