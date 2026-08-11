# Day 3 - Authorization policies and claims

This piece separates authentication from authorization:

- Authentication proves who the caller is through JWT bearer auth.
- Authorization decides what the caller can do through named policies.

## Policies

Claim-based policy:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-edit-quotes", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "quotes.write");
    });
```

Custom requirement policy:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-delete-own-quote", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new OwnQuoteRequirement());
    });
```

The custom `OwnQuoteAuthorizationHandler` reads the `quoteId` route value and asks `IQuoteOwnershipService` whether the authenticated user owns that quote.

## Protected endpoints

```csharp
app.MapPut("/quotes/{quoteId:int}", ...)
    .RequireAuthorization("can-edit-quotes");

app.MapDelete("/quotes/{quoteId:int}", ...)
    .RequireAuthorization("can-delete-own-quote");
```

## Tests showing 403

Run:

```bash
dotnet test
```

The tests prove both failure modes:

- `EditQuoteWithoutWriteScope_ReturnsForbidden` sends a valid JWT with `scope=quotes.read`, calls `PUT /quotes/1`, and expects `403 Forbidden`.
- `DeleteQuoteOwnedBySomeoneElse_ReturnsForbidden` sends a valid JWT for `user-999`, calls `DELETE /quotes/1`, and expects `403 Forbidden` because quote `1` belongs to `user-123`.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece2

## Notes for mentor

The policy names describe stable business capabilities. The claims and ownership lookup are implementation details behind those policies.

## What did I learn this session?

The useful click was that roles and claims are inputs, but policies are the API's contract for permission checks.

## What would break this?

If scope formatting changes to a space-separated `scp` claim from Entra ID, the claim policy would need to check `scp` too. The ownership handler also assumes the route value is named `quoteId`.
