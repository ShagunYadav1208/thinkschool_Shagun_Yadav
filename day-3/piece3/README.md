# Day 3 - Lock down the API end-to-end

This piece wraps the auth work into one Quotes API:

- Dual JWT schemes: self-issued internal JWT and Entra JWT for SPA callers.
- Refresh-token rotation with reuse detection and refresh-family revocation.
- Policies on every quote-mutating endpoint.
- `WebApplicationFactory` integration tests for 401, 403, 200, expired token, and revoked refresh chain.

## Run

```bash
dotnet test
```

## What is covered

| Case | Test | Expected |
| --- | --- | --- |
| Anonymous mutating request | `AnonymousMutatingRequest_ReturnsUnauthorized` | `401` |
| Authenticated but wrong policy | `AuthenticatedCallerWithWrongPolicy_ReturnsForbidden` | `403` |
| Authenticated with right policy | `AuthenticatedCallerWithRightPolicy_ReturnsOk` | `200` |
| Expired token | `ExpiredAccessToken_ReturnsUnauthorized` | `401` |
| Revoked refresh chain | `ReusedRefreshToken_RevokesRefreshChain` | `401` |

## Auth setup

`SmartBearer` uses `AddPolicyScheme` to choose between internal JWT and Entra JWT by reading the issuer:

```csharp
.AddPolicyScheme(SmartBearerScheme, "Issuer based JWT selector", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        var token = GetBearerToken(context.Request.Headers.Authorization.ToString());
        var issuer = ReadIssuer(token);
        return IsEntraIssuer(issuer) ? EntraJwtScheme : InternalJwtScheme;
    };
})
```

The Entra config in `appsettings.json` uses meaningful placeholders. A real Entra call needs a real tenant, client ID, audience, and access token from Azure CLI or a SPA.

## Policies

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-edit-quotes", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "quotes.write");
    })
    .AddPolicy("can-delete-own-quote", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new DeleteOwnQuoteRequirement());
    });
```

## PR and CI

PR URL: paste after pushing.

CI run URL: paste after pushing.

PR note: this is solid.

The CI workflow lives at the repository's actual root — `.github/workflows/day3-piece3.yml` relative to the `thinkschool_Shagun_Yadav` repo root, alongside the existing `ci.yml` (which is scoped to `day-4/piece1`). It triggers on push/PR changes under `day-3/piece3/**` and runs `dotnet test day-3/piece3`.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece3

## Notes for mentor

The tests use internal JWTs so they run deterministically in CI without contacting Microsoft Entra. The production API still has the Entra bearer scheme wired, and Entra tokens will route to that scheme based on the issuer claim.

## What did I learn this session?

The useful click was seeing the full chain: authentication establishes identity, policy authorization gates mutation, and refresh-token reuse detection protects the session after a stolen token is replayed.

## What would break this?

The Entra path needs real app registration values before a live SPA token works. If Entra sends scopes in `scp` as a space-separated string, the policy would need to accept that format in addition to repeated `scope` claims.
