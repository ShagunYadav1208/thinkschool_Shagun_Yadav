using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using System.Security.Cryptography;
using System.Text;

namespace QuotesApi.Services;

public sealed class RefreshTokenService(
    QuotesDbContext db,
    IJwtTokenService jwt,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
    public async Task<TokenResponse> IssueForLoginAsync(User user, CancellationToken cancellationToken)
    {
        var rawToken = CreateRawToken();
        db.RefreshTokens.Add(CreateStoredToken(user.Id, Guid.NewGuid(), rawToken));
        await db.SaveChangesAsync(cancellationToken);
        return CreateResponse(user, rawToken);
    }

    public async Task<RefreshResult> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = Hash(rawRefreshToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(token => token.Token == tokenHash, cancellationToken);
        if (token is null || token.ExpiresAt <= DateTimeOffset.UtcNow)
            return new RefreshResult(null, false);

        if (token.RevokedAt is not null)
        {
            if (token.ReplacedByToken is not null)
            {
                logger.LogWarning("Refresh token reuse detected for family {FamilyId}.", token.FamilyId);
                var familyTokens = await db.RefreshTokens
                    .Where(candidate => candidate.FamilyId == token.FamilyId && candidate.RevokedAt == null)
                    .ToListAsync(cancellationToken);
                foreach (var familyToken in familyTokens)
                    familyToken.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return new RefreshResult(null, true);
            }

            return new RefreshResult(null, false);
        }

        var user = await db.Users.SingleAsync(user => user.Id == token.UserId, cancellationToken);
        var replacementRawToken = CreateRawToken();
        var replacement = CreateStoredToken(user.Id, token.FamilyId, replacementRawToken);
        token.RevokedAt = DateTimeOffset.UtcNow;
        token.ReplacedByToken = replacement.Token;
        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);

        return new RefreshResult(CreateResponse(user, replacementRawToken), false);
    }

    public async Task<bool> RevokeAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        var token = await db.RefreshTokens.SingleOrDefaultAsync(candidate => candidate.Token == Hash(rawRefreshToken), cancellationToken);
        if (token is null || token.RevokedAt is not null)
            return false;

        token.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private RefreshToken CreateStoredToken(int userId, Guid familyId, string rawToken) => new()
    {
        UserId = userId,
        FamilyId = familyId,
        Token = Hash(rawToken),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
    };

    private TokenResponse CreateResponse(User user, string refreshToken) =>
        new(jwt.CreateAccessToken(user), refreshToken, jwt.AccessTokenLifetimeSeconds);

    private static string CreateRawToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
