using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class AuthEndpointExtensions
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, QuotesDbContext db, IRefreshTokenService refreshTokens, CancellationToken cancellationToken) =>
        {
            var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Email == request.Email.Trim().ToLowerInvariant(), cancellationToken);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return Results.Unauthorized();

            return Results.Ok(await refreshTokens.IssueForLoginAsync(user, cancellationToken));
        }).AllowAnonymous();

        app.MapPost("/api/auth/refresh", async (RefreshRequest request, IRefreshTokenService refreshTokens, CancellationToken cancellationToken) =>
        {
            var result = await refreshTokens.RefreshAsync(request.RefreshToken, cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Tokens) : Results.Unauthorized();
        }).AllowAnonymous();

        app.MapPost("/api/auth/logout", async (RefreshRequest request, IRefreshTokenService refreshTokens, CancellationToken cancellationToken) =>
        {
            await refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
