using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ParkFlow.Api.Security;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The single accepted key. Populated from configuration (`Security:ApiKey`) in Program.cs.
    /// Empty means "not configured" — the handler rejects every request rather than accepting
    /// anything, so a missing configuration value fails closed instead of open.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Interim authentication for the ParkFlow API: a single shared key sent as the
/// <c>X-Api-Key</c> header. This authenticates a request as coming from a trusted client of the
/// API — it does not identify *which end user* is calling, so it cannot by itself close the BOLA
/// gap documented in ../../THREAT-MODEL.md (reservation actions don't check ownership). See that
/// document, section 5, for why this scheme was chosen for this pass instead of full OAuth2/OIDC.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrEmpty(Options.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("API key authentication is not configured."));
        }

        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var provided) ||
            provided.Count != 1 ||
            string.IsNullOrEmpty(provided[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail($"Missing {ApiKeyAuthenticationDefaults.HeaderName} header."));
        }

        // Fixed-time comparison: a naive `==` leaks how many leading bytes matched via response
        // timing, letting an attacker recover the key one byte at a time.
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided[0]!);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(Options.ApiKey);
        var isValid = providedBytes.Length == expectedBytes.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);

        if (!isValid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(ApiKeyAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
