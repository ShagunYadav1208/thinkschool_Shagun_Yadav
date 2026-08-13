using System.Text;
using Microsoft.Extensions.Options;

namespace QuotesIntegrationApi.Configuration;

/// <summary>
/// Registered with <c>ValidateOnStart()</c> so a missing/too-short signing key fails the app at
/// startup with a clear message, instead of on whatever request first happens to hit the JWT
/// bearer handler (Piece 5's behavior: an ad-hoc <c>throw</c> sitting in the middle of Program.cs).
/// </summary>
public sealed class JwtOptionsValidation : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
            return ValidateOptionsResult.Fail("Jwt:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            return ValidateOptionsResult.Fail("Jwt:Audience is required.");

        if (string.IsNullOrWhiteSpace(options.Key))
            return ValidateOptionsResult.Fail("Jwt:Key is required (set it via user-secrets or Key Vault, never appsettings.json).");

        if (Encoding.UTF8.GetByteCount(options.Key) < 32)
            return ValidateOptionsResult.Fail("Jwt:Key must be at least 32 bytes for HMAC signing.");

        if (options.AccessTokenLifetime <= TimeSpan.Zero)
            return ValidateOptionsResult.Fail("Jwt:AccessTokenLifetime must be a positive duration.");

        return ValidateOptionsResult.Success;
    }
}
