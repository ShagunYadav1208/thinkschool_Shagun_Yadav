namespace QuotesIntegrationApi.Configuration;

/// <summary>
/// Bound from the "Jwt" configuration section. <see cref="Key"/> never appears in
/// appsettings.json — locally it comes from `dotnet user-secrets`, in production from a Key Vault
/// reference (see Program.cs and the README).
/// </summary>
public sealed record JwtOptions
{
    // C# 14's `field` keyword: these are still ordinary auto-properties (the configuration binder
    // sets them the same way), but `init` can now trim the incoming value without a manually
    // declared backing field. Real motivation, not just a syntax demo: env vars and secret stores
    // occasionally carry trailing whitespace or a stray newline from how they were set, and a JWT
    // issuer/audience/key with invisible trailing whitespace fails validation in a way that's
    // miserable to debug.
    public required string Issuer { get; init => field = value.Trim(); }

    public required string Audience { get; init => field = value.Trim(); }

    public required string Key { get; init => field = value.Trim(); }

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
