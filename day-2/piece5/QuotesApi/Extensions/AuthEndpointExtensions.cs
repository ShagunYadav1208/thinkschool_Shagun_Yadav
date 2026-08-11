using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (
            LoginRequest request,
            QuotesDbContext db,
            IJwtTokenService tokens,
            CancellationToken cancellationToken) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

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
    }
}
