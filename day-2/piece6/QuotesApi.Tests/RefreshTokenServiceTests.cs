using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Tests;

public class RefreshTokenServiceTests
{
    [Fact]
    public async Task RefreshingAReplacedToken_RevokesEveryActiveTokenInItsFamily()
    {
        await using var db = new QuotesDbContext(new DbContextOptionsBuilder<QuotesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new User { Email = "user@example.com", PasswordHash = "not-used" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var initial = await service.IssueForLoginAsync(user, CancellationToken.None);
        var rotated = await service.RefreshAsync(initial.refresh_token, CancellationToken.None);
        var reuse = await service.RefreshAsync(initial.refresh_token, CancellationToken.None);

        Assert.True(rotated.IsSuccess);
        Assert.True(reuse.ReuseDetected);
        Assert.All(db.RefreshTokens, token => Assert.NotNull(token.RevokedAt));

        var replacementAttempt = await service.RefreshAsync(rotated.Tokens!.refresh_token, CancellationToken.None);
        Assert.False(replacementAttempt.IsSuccess);
    }

    private static RefreshTokenService CreateService(QuotesDbContext db) => new(
        db,
        new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "Tests",
            Audience = "Tests",
            Key = "this-is-a-test-only-256-bit-signing-key!",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        })),
        NullLogger<RefreshTokenService>.Instance);
}
