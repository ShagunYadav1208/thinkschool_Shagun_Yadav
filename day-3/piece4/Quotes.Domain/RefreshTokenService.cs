using System.Security.Cryptography;
using System.Text;

namespace Quotes.Domain;

public sealed class RefreshTokenService(IClock clock, IRefreshReuseNotifier reuseNotifier)
{
    private readonly List<RefreshTokenRecord> _tokens = [];

    public TokenPair Issue(string userId)
    {
        var rawRefreshToken = CreateRawRefreshToken();
        _tokens.Add(CreateStoredToken(userId, Guid.NewGuid(), rawRefreshToken));

        return new TokenPair(CreateAccessToken(userId), rawRefreshToken);
    }

    public RefreshResult Refresh(string rawRefreshToken)
    {
        var tokenHash = Hash(rawRefreshToken);
        var token = _tokens.SingleOrDefault(candidate => candidate.TokenHash == tokenHash);

        if (token is null || token.ExpiresAt <= clock.UtcNow)
        {
            return new RefreshResult(null, false);
        }

        if (token.RevokedAt is not null)
        {
            if (token.ReplacedByTokenHash is not null)
            {
                foreach (var activeFamilyToken in _tokens.Where(candidate =>
                    candidate.FamilyId == token.FamilyId && candidate.RevokedAt is null))
                {
                    activeFamilyToken.RevokedAt = clock.UtcNow;
                }

                reuseNotifier.ReuseDetected(token.FamilyId, token.UserId);
                return new RefreshResult(null, true);
            }

            return new RefreshResult(null, false);
        }

        var replacementRawToken = CreateRawRefreshToken();
        var replacement = CreateStoredToken(token.UserId, token.FamilyId, replacementRawToken);

        token.RevokedAt = clock.UtcNow;
        token.ReplacedByTokenHash = replacement.TokenHash;
        _tokens.Add(replacement);

        return new RefreshResult(new TokenPair(CreateAccessToken(token.UserId), replacementRawToken), false);
    }

    private RefreshTokenRecord CreateStoredToken(string userId, Guid familyId, string rawRefreshToken) => new()
    {
        UserId = userId,
        FamilyId = familyId,
        TokenHash = Hash(rawRefreshToken),
        ExpiresAt = clock.UtcNow.AddDays(7)
    };

    private static string CreateAccessToken(string userId) => $"access-token-for-{userId}-{Guid.NewGuid():N}";

    private static string CreateRawRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
