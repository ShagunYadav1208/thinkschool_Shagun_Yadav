# Day 3 - Wire Entra ID as the identity provider

This piece keeps two JWT schemes:

- `InternalJwt` validates the API's own HMAC-signed tokens for internal callers.
- `EntraJwt` validates Microsoft Entra ID access tokens for SPA/customer-facing callers.
- `SmartBearer` is an `AddPolicyScheme` selector that reads the token issuer and forwards Entra-issued tokens to `EntraJwt`; everything else falls back to `InternalJwt`.

## Configure

Register the API in Microsoft Entra ID, then replace the placeholders in `appsettings.json`:

```json
"EntraId": {
  "TenantId": "<tenant-id>",
  "ClientId": "<api-app-client-id>",
  "Audience": "api://<api-app-client-id>"
}
```

For local development, keep the internal JWT key at least 32 bytes.

## Program.cs auth setup

```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = SmartBearerScheme;
        options.DefaultChallengeScheme = SmartBearerScheme;
    })
    .AddPolicyScheme(SmartBearerScheme, "Issuer based JWT selector", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var token = EntraTokenRouting.GetBearerToken(context.Request.Headers.Authorization);
            var issuer = EntraTokenRouting.ReadIssuer(token);

            return EntraTokenRouting.IsEntraIssuer(issuer) ? EntraJwtScheme : InternalJwtScheme;
        };
    })
    .AddJwtBearer(InternalJwtScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(internalKey)),
            ValidateIssuer = true,
            ValidIssuer = internalIssuer,
            ValidateAudience = true,
            ValidAudience = internalAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddJwtBearer(EntraJwtScheme, options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";
        options.Audience = entraAudience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{entraTenantId}/v2.0",
                $"https://sts.windows.net/{entraTenantId}/"
            ],
            ValidateAudience = true,
            ValidAudiences = EntraTokenRouting.GetAllowedEntraAudiences(entraAudience),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    });
```

## Unit test coverage

`Day3Piece1.Tests` covers the pure routing/parsing logic that used to live as unmarked local functions in `Program.cs` (now extracted to `EntraTokenRouting.cs` so it's testable in isolation, with zero network calls and no token that impersonates a real identity provider):

- `IsEntraIssuer` correctly classifies both real Microsoft issuer prefixes (`login.microsoftonline.com`, `sts.windows.net`, case-insensitively) and non-Entra/null issuers
- `GetBearerToken` correctly extracts or rejects malformed `Authorization` headers
- `ReadIssuer` correctly parses a well-formed token's issuer and returns `null` instead of throwing on garbage input
- `GetAllowedEntraAudiences` produces both the `api://...` and bare-GUID audience forms

Run with `dotnet test` from `Day3Piece1.Tests/`.

## Test with curl

This has been end-to-end verified with a real Microsoft Entra app registration and a genuine, live-issued access token (registered temporarily in a real tenant for this test, then deleted afterward — this repo does not contain any real tenant ID, client ID, or token).

Steps used for the real test:

1. `az ad app create --display-name "<app-name>" --sign-in-audience AzureADMyOrg` to register the app, then `az ad app update --id <appId> --identifier-uris api://<appId>` to set the App ID URI.
2. `az ad sp create --id <appId>` to create the app's service principal (required before the tenant will issue tokens for it).
3. Exposed a delegated scope (`access_as_user`) via a Microsoft Graph `PATCH` on `applications/<objectId>` (`api.oauth2PermissionScopes`), since a fresh app registration has none by default.
4. `az login --tenant <tenantId> --scope api://<appId>/access_as_user --use-device-code`, then completed the device-code consent flow (user-level consent, not tenant-wide admin consent) in a browser.
5. `az account get-access-token --scope api://<appId>/access_as_user` to mint the real token.
6. Ran the API with `EntraId__TenantId`, `EntraId__ClientId`, and `EntraId__Audience` set via environment variables (not committed to `appsettings.json`) to the real registered values, then called it with the real token.

Real result:

```bash
curl -i http://localhost:PORT/api/spa-profile \
  -H "Authorization: Bearer $ENTRA_TOKEN"
```

```
HTTP/1.1 200 OK
{"scheme":"EntraJwt","message":"Entra ID access token accepted.", ...}
```

`GET /api/me` also succeeded with the same token (proving `SmartBearer` routed it to `EntraJwt` by issuer), and `GET /api/internal-report` correctly rejected it with `401` (`"The issuer '...' is invalid"`), confirming the two schemes stay isolated even with a real, validly-signed token.

Internal caller test:

```bash
INTERNAL_TOKEN=$(curl -sk https://localhost:5001/auth/internal-token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"internal-worker","clientSecret":"local-dev-secret"}' \
  | jq -r .access_token)

curl -k https://localhost:5001/api/internal-report \
  -H "Authorization: Bearer $INTERNAL_TOKEN"
```

To re-run the Entra test yourself against your own tenant: replace the placeholder `TenantId`, `ClientId`, and `Audience` in `appsettings.json` (or set them via `EntraId__TenantId` / `EntraId__ClientId` / `EntraId__Audience` environment variables) with values from your own Entra app registration, then get a token through Azure CLI or a SPA login flow as shown above.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece1

## Notes for mentor

The API trusts both its own JWTs and Entra ID JWTs. `AddPolicyScheme` chooses the validation scheme by reading the untrusted token issuer only for routing; the selected `JwtBearer` handler still performs the real signature, issuer, audience, and lifetime validation.

## What did I learn this session?

The part that clicked: OAuth/OIDC tokens are still JWTs at the API boundary, but the trust decision moves from my signing key to Entra's issuer metadata and signing keys.

## What would break this?

Using the wrong app registration audience, tenant ID, or token type would fail validation. A multi-tenant API would also need a broader issuer validation strategy than the single-tenant `ValidIssuers` list here.

This was not just a hypothetical: the real access token minted during testing came back with issuer `https://sts.windows.net/{tenant}/` (the older v1.0 token format), not `https://login.microsoftonline.com/{tenant}/v2.0`. Without both formats in `ValidIssuers`, this real, validly-signed token would have been rejected — a fresh app registration doesn't default to v2.0 tokens unless `requestedAccessTokenVersion` is explicitly set on the app manifest.
