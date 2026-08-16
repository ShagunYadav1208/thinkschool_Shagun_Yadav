namespace Quotes.Domain;

public sealed class RefreshTokenService(
    IRefreshTokenStore refreshTokens,
    IUserStore users,
    ITokenService tokens) : IRefreshTokenService
{
    public TokenResponse Issue(User user)
    {
        var rawRefreshToken = tokens.CreateRefreshToken();
        refreshTokens.Add(CreateStoredToken(user.Id, Guid.NewGuid(), rawRefreshToken));
        return CreateResponse(user, rawRefreshToken);
    }

    public RefreshResult Refresh(string rawRefreshToken)
    {
        var tokenHash = tokens.HashRefreshToken(rawRefreshToken);
        var token = refreshTokens.FindByHash(tokenHash);

        if (token is null || token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return new RefreshResult(null, false);
        }

        if (token.RevokedAt is not null)
        {
            if (token.ReplacedByTokenHash is not null)
            {
                foreach (var activeFamilyToken in refreshTokens.ActiveFamilyTokens(token.FamilyId))
                {
                    activeFamilyToken.RevokedAt = DateTimeOffset.UtcNow;
                }

                return new RefreshResult(null, true);
            }

            return new RefreshResult(null, false);
        }

        var user = users.FindById(token.UserId);
        if (user is null)
        {
            return new RefreshResult(null, false);
        }

        var replacementRawToken = tokens.CreateRefreshToken();
        var replacement = CreateStoredToken(user.Id, token.FamilyId, replacementRawToken);
        token.RevokedAt = DateTimeOffset.UtcNow;
        token.ReplacedByTokenHash = replacement.TokenHash;
        refreshTokens.Add(replacement);

        return new RefreshResult(CreateResponse(user, replacementRawToken), false);
    }

    private RefreshTokenRecord CreateStoredToken(string userId, Guid familyId, string rawRefreshToken) => new()
    {
        UserId = userId,
        FamilyId = familyId,
        TokenHash = tokens.HashRefreshToken(rawRefreshToken),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
    };

    private TokenResponse CreateResponse(User user, string refreshToken) =>
        new(tokens.CreateAccessToken(user, user.Scopes), refreshToken, tokens.AccessTokenLifetimeSeconds);
}
