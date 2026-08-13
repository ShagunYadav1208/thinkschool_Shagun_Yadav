using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuotesIntegrationApi.Configuration;

namespace QuotesIntegrationApi.Services;

/// <summary>
/// Registered as a singleton — created once, lives for the app's lifetime — so it can't just read
/// <see cref="JwtOptions"/> in its constructor and cache the values forever; a config reload
/// (e.g. an Azure App Configuration refresh, or a mounted config file changing) would leave it
/// silently signing tokens with a stale issuer or lifetime. <see cref="IOptionsMonitor{TOptions}"/>
/// solves both halves of that: <c>CurrentValue</c> always reads the latest bound values, and
/// <c>OnChange</c> is the notification hook a singleton needs to react to a live change instead of
/// just quietly using new values on the next call.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly IOptionsMonitor<JwtOptions> _jwtOptions;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IOptionsMonitor<JwtOptions> jwtOptions, ILogger<TokenService> logger)
    {
        _jwtOptions = jwtOptions;
        _logger = logger;

        _jwtOptions.OnChange(_ =>
            _logger.LogWarning("Jwt configuration changed at runtime; new tokens will use the updated settings."));
    }

    public string CreateToken(string subject)
    {
        var jwt = _jwtOptions.CurrentValue;

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim(ClaimTypes.NameIdentifier, subject)
        };

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(jwt.AccessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
