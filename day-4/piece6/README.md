# Day 4 - Configuration done right

This piece builds on Day 4 Piece 5's `QuotesIntegrationApi` and replaces its ad-hoc
`builder.Configuration["Jwt:Issuer"]`-style reads (three separate string-indexer calls, plus a
hand-rolled `if (...) throw` for key length sitting in the middle of `Program.cs`) with the
`IOptions` pattern: typed options classes, startup validation, and each of `IOptions<T>` /
`IOptionsSnapshot<T>` / `IOptionsMonitor<T>` used where it actually fits — not just to check three
boxes.

## Run

```bash
dotnet user-secrets set "Jwt:Key" "some-32-byte-minimum-development-key!" --project day-4/piece6/QuotesIntegrationApi
dotnet run --project day-4/piece6/QuotesIntegrationApi
```

Without that secret set, the app fails immediately at startup (verified — see below), not on the
first request that happens to need it.

## The `JwtOptions` class

```csharp
namespace QuotesIntegrationApi.Configuration;

public sealed record JwtOptions
{
    // C# 14's `field` keyword: still an ordinary auto-property as far as the configuration binder
    // is concerned, but `init` can trim the incoming value without a manually declared backing
    // field. Real motivation: env vars and secret stores occasionally carry trailing whitespace or
    // a stray newline, and a JWT issuer/audience/key with invisible trailing whitespace fails
    // validation in a way that's miserable to debug.
    public required string Issuer { get; init => field = value.Trim(); }
    public required string Audience { get; init => field = value.Trim(); }
    public required string Key { get; init => field = value.Trim(); }

    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
}
```

## The appsettings section

`appsettings.json` — note **`Key` is not here**:

```json
"Jwt": {
  "Issuer": "QuotesIntegrationApi",
  "Audience": "QuotesIntegrationApi.Client",
  "AccessTokenLifetime": "00:15:00"
}
```

Locally, `Jwt:Key` comes from `dotnet user-secrets` (above). In production it's a Key Vault
reference set as an App Service application setting — which is itself an environment variable, so
it still wins over any `appsettings*.json` file per the precedence order the exercise names
(env vars > `appsettings.{Environment}.json` > `appsettings.json`).

## DI registration

```csharp
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidation>();
```

`JwtOptionsValidation` checks all four fields (non-empty issuer/audience/key, key ≥ 32 bytes,
positive lifetime) and `ValidateOnStart()` runs it during host startup, not lazily on first use.
Verified by actually running the app with no `Jwt:Key` configured at all:

```
Microsoft.Extensions.Options.OptionsValidationException: Jwt:Key is required (set it via
user-secrets or Key Vault, never appsettings.json).
   at ... Microsoft.Extensions.Hosting.Internal.Host.StartAsync(CancellationToken cancellationToken)
```

The app never gets as far as binding a port — exactly the "fail fast, fail clearly" behavior
`ValidateOnStart()` is for.

## How it's injected in a service

Three different consumers, three different `IOptions` flavors, each chosen for what the consumer
actually is:

**`IOptions<JwtOptions>`** — in `JwtBearerOptionsSetup`, a one-shot configuration class the
framework calls once when it builds `JwtBearerOptions` for the auth scheme:

```csharp
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
```
registered with `builder.Services.ConfigureOptions<JwtBearerOptionsSetup>();` — `Program.cs` now
just calls `.AddAuthentication(...).AddJwtBearer()` with no inline lambda at all.

**`IOptionsMonitor<JwtOptions>`** — in `TokenService`, a **singleton** that lives for the whole
app's lifetime and must never cache a stale value:

```csharp
public sealed class TokenService : ITokenService
{
    private readonly IOptionsMonitor<JwtOptions> _jwtOptions;

    public TokenService(IOptionsMonitor<JwtOptions> jwtOptions, ILogger<TokenService> logger)
    {
        _jwtOptions = jwtOptions;
        _jwtOptions.OnChange(_ =>
            logger.LogWarning("Jwt configuration changed at runtime; new tokens will use the updated settings."));
    }

    public string CreateToken(string subject)
    {
        var jwt = _jwtOptions.CurrentValue; // always the latest bound value, never stale
        // ... builds and signs the JWT using jwt.Issuer / jwt.Audience / jwt.Key / jwt.AccessTokenLifetime
    }
}
```

**`IOptionsSnapshot<QuoteValidationOptions>`** — in the `POST /api/quotes` minimal API handler, a
**per-request scoped** read for limits that are safe to change without a restart:

```csharp
quotes.MapPost("/", async (
    CreateQuoteRequest request,
    /* ... */
    IOptionsSnapshot<QuoteValidationOptions> quoteValidationOptions,
    CancellationToken cancellationToken) =>
{
    var limits = quoteValidationOptions.Value;
    // ... errors["author"] = [$"Author must be {limits.MaxAuthorLength} characters or fewer."];
});
```

Verified end-to-end against a running instance: issued a token, decoded its payload (`exp - nbf` =
900 seconds, matching the configured `"00:15:00"`), then sent a 101-character author and got back
`"Author must be 100 characters or fewer."` — the exact number from `QuoteValidationOptions`, not a
hardcoded literal anymore.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece6

## Notes for mentor

The existing `Quotes.Tests.Integration` suite forces `UseEnvironment("Testing")`, and user-secrets
only auto-load in `Development` — so removing `Jwt:Key` from `appsettings.json` broke the tests
until I gave `QuotesApiFactory` its own throwaway key via `ConfigureAppConfiguration(...).AddInMemoryCollection(...)`.
That's the correct fix (tests get their own config, not a secret checked into a file), not a
workaround — flagging it since it's the one place I touched code outside `Program.cs`/`Configuration`/`Services`.

## What did I learn this session?

`ValidateOnStart()` plus a custom `IValidateOptions<T>` turns "the app is broken in a way nobody
notices until the first login attempt three hours after a bad deploy" into "the deploy itself
fails, immediately, with the exact reason." That's a categorically different failure mode from the
`if (...) throw` Piece 5 had sitting inline in `Program.cs` — same check, but one only fires when
someone happens to hit that code path.

## What would break this?

`IOptionsMonitor.OnChange` fires on *any* reload of the underlying configuration provider — if this
app ever adds a second frequently-changing config section reloaded from the same source (e.g. a
feature-flag file polled every few seconds), `TokenService`'s `OnChange` callback fires on every one
of those reloads too, even when `Jwt` itself didn't change, and logs a warning each time. Worth
comparing old vs. new values before logging if that section ever gets noisy neighbors.
