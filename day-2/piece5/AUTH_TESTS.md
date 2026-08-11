# JWT authentication verification

The API was run locally at `http://127.0.0.1:5099`. The development seed account is `demo@quotes.local` with password `DemoPassword123!`.

## Login endpoint

```csharp
app.MapPost("/api/auth/login", async (
    LoginRequest request,
    QuotesDbContext db,
    IJwtTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var user = await db.Users.SingleOrDefaultAsync(
        candidate => candidate.Email == request.Email.Trim().ToLowerInvariant(),
        cancellationToken);

    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Unauthorized();

    var issued = tokens.Issue(user);
    db.RefreshTokens.Add(new RefreshToken
    {
        UserId = user.Id,
        TokenHash = tokens.HashRefreshToken(issued.RefreshToken),
        ExpiresAt = issued.RefreshTokenExpiresAt
    });
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new LoginResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn));
}).AllowAnonymous();
```

## Protected quote endpoint checks

```bash
# no access token
curl -i -X POST http://127.0.0.1:5099/api/quotes/ \
  -H "Content-Type: application/json" \
  --data '{"author":"Ada Lovelace","text":"A quote"}'

HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer
```

```bash
# use access_token returned from POST /api/auth/login
curl -i -X POST http://127.0.0.1:5099/api/quotes/ \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <access_token>" \
  --data '{"author":"Ada Lovelace","text":"A quote"}'

HTTP/1.1 200 OK
```

```bash
# signed token with an exp claim in the past
curl -i -X POST http://127.0.0.1:5099/api/quotes/ \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <expired_access_token>" \
  --data '{"author":"Expired Token","text":"Rejected"}'

HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer error="invalid_token", error_description="The token expired at '08/11/2026 06:04:00'"
```
