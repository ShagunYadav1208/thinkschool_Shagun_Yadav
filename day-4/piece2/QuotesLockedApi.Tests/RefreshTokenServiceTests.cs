namespace QuotesLockedApi.Tests;

// Rotation and reuse-detection are already exercised end-to-end by AuthIntegrationTests via real
// HTTP calls; these fill in the Refresh() branches that aren't reachable from the happy-path flow
// a normal client would ever trigger.
public sealed class RefreshTokenServiceTests
{
    private readonly InMemoryUserStore _users = new();
    private readonly InMemoryRefreshTokenStore _refreshTokens = new();
    private readonly TokenService _tokens = new(new JwtOptions("issuer", "audience", "unit-test-only-32-byte-minimum-key!"));
    private readonly RefreshTokenService _sut;

    public RefreshTokenServiceTests()
    {
        _sut = new RefreshTokenService(_refreshTokens, _users, _tokens);
    }

    [Fact]
    public void Refresh_UnknownToken_ReturnsNullWithoutReuseDetected()
    {
        var result = _sut.Refresh("a-token-that-was-never-issued");

        Assert.Null(result.Tokens);
        Assert.False(result.ReuseDetected);
    }

    [Fact]
    public void Refresh_ExpiredToken_ReturnsNullWithoutReuseDetected()
    {
        var raw = _tokens.CreateRefreshToken();
        _refreshTokens.Add(new RefreshTokenRecord
        {
            UserId = "user-123",
            FamilyId = Guid.NewGuid(),
            TokenHash = _tokens.HashRefreshToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        });

        var result = _sut.Refresh(raw);

        Assert.Null(result.Tokens);
        Assert.False(result.ReuseDetected);
    }

    [Fact]
    public void Refresh_RevokedWithoutReplacement_ReturnsNullWithoutReuseDetected()
    {
        // A revoked token with no replacement-hash on record isn't the reuse-of-a-rotated-token
        // case (that always sets ReplacedByTokenHash) — it's a token revoked some other way
        // (e.g. an explicit logout-everywhere), so it should just fail closed, not trip reuse
        // detection.
        var raw = _tokens.CreateRefreshToken();
        _refreshTokens.Add(new RefreshTokenRecord
        {
            UserId = "user-123",
            FamilyId = Guid.NewGuid(),
            TokenHash = _tokens.HashRefreshToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ReplacedByTokenHash = null
        });

        var result = _sut.Refresh(raw);

        Assert.Null(result.Tokens);
        Assert.False(result.ReuseDetected);
    }

    [Fact]
    public void Refresh_TokenForUnknownUser_ReturnsNull()
    {
        var raw = _tokens.CreateRefreshToken();
        _refreshTokens.Add(new RefreshTokenRecord
        {
            UserId = "ghost-user",
            FamilyId = Guid.NewGuid(),
            TokenHash = _tokens.HashRefreshToken(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        });

        var result = _sut.Refresh(raw);

        Assert.Null(result.Tokens);
        Assert.False(result.ReuseDetected);
    }
}
