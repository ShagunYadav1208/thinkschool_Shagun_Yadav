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
            var token = GetBearerToken(context.Request.Headers.Authorization);
            var issuer = ReadIssuer(token);

            return IsEntraIssuer(issuer) ? EntraJwtScheme : InternalJwtScheme;
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
            ValidAudiences = GetAllowedEntraAudiences(entraAudience),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    });
```

## Test with curl

Note: there is no real Entra access token in this local repo. The Entra test below will only succeed after replacing the placeholder `TenantId`, `ClientId`, and `Audience` in `appsettings.json` with values from an actual Microsoft Entra app registration, then getting a real access token through Azure CLI or a SPA login flow.

Run the API:

```bash
dotnet run
```

Get an Entra-issued token:

```bash
ENTRA_TOKEN=$(az account get-access-token --resource api://<api-app-client-id> --query accessToken -o tsv)
```

Call the Entra-protected endpoint:

```bash
curl -k https://localhost:5001/api/spa-profile \
  -H "Authorization: Bearer $ENTRA_TOKEN"
```

Expected result:

```json
{
  "scheme": "EntraJwt",
  "message": "Entra ID access token accepted."
}
```

Internal caller test:

```bash
INTERNAL_TOKEN=$(curl -sk https://localhost:5001/auth/internal-token \
  -H "Content-Type: application/json" \
  -d '{"clientId":"internal-worker","clientSecret":"local-dev-secret"}' \
  | jq -r .access_token)

curl -k https://localhost:5001/api/internal-report \
  -H "Authorization: Bearer $INTERNAL_TOKEN"
```

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece1

## Notes for mentor

The API trusts both its own JWTs and Entra ID JWTs. `AddPolicyScheme` chooses the validation scheme by reading the untrusted token issuer only for routing; the selected `JwtBearer` handler still performs the real signature, issuer, audience, and lifetime validation.

## What did I learn this session?

The part that clicked: OAuth/OIDC tokens are still JWTs at the API boundary, but the trust decision moves from my signing key to Entra's issuer metadata and signing keys.

## What would break this?

Using the wrong app registration audience, tenant ID, or token type would fail validation. A multi-tenant API would also need a broader issuer validation strategy than the single-tenant `ValidIssuers` list here.
