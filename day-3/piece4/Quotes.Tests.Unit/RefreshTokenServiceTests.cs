using FluentAssertions;
using NSubstitute;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public class RefreshTokenServiceTests
{
    [Fact]
    public void Issue_ForUser_StoresNewTokenAndReturnsResponse()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        tokens.CreateRefreshToken().Returns("raw-refresh-token");
        tokens.CreateAccessToken(Arg.Any<User>(), Arg.Any<string[]>()).Returns("access-token");
        tokens.HashRefreshToken("raw-refresh-token").Returns("hashed-refresh-token");
        tokens.AccessTokenLifetimeSeconds.Returns(900);
        var service = new RefreshTokenService(store, users, tokens);
        var user = new User("user-1", "user@example.com", ["quotes.read"]);

        var response = service.Issue(user);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("raw-refresh-token");
        response.ExpiresIn.Should().Be(900);
        store.Received(1).Add(Arg.Is<RefreshTokenRecord>(record =>
            record.UserId == "user-1" && record.TokenHash == "hashed-refresh-token"));
    }

    [Fact]
    public void Refresh_WithUnknownToken_ReturnsFailure()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        tokens.HashRefreshToken("unknown-token").Returns("unknown-hash");
        store.FindByHash("unknown-hash").Returns((RefreshTokenRecord?)null);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("unknown-token");

        result.Tokens.Should().BeNull();
        result.ReuseDetected.Should().BeFalse();
    }

    [Fact]
    public void Refresh_WithExpiredToken_ReturnsFailure()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        tokens.HashRefreshToken("expired-token").Returns("expired-hash");
        var expiredRecord = new RefreshTokenRecord
        {
            TokenHash = "expired-hash",
            UserId = "user-1",
            FamilyId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        store.FindByHash("expired-hash").Returns(expiredRecord);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("expired-token");

        result.Tokens.Should().BeNull();
        result.ReuseDetected.Should().BeFalse();
    }

    [Fact]
    public void Refresh_WithValidToken_RotatesAndReturnsNewTokens()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        var user = new User("user-1", "user@example.com", ["quotes.read"]);
        var familyId = Guid.NewGuid();
        var validRecord = new RefreshTokenRecord
        {
            TokenHash = "valid-hash",
            UserId = "user-1",
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        tokens.HashRefreshToken("valid-token").Returns("valid-hash");
        tokens.HashRefreshToken("new-raw-token").Returns("new-hash");
        tokens.CreateRefreshToken().Returns("new-raw-token");
        tokens.CreateAccessToken(user, user.Scopes).Returns("new-access-token");
        tokens.AccessTokenLifetimeSeconds.Returns(900);
        store.FindByHash("valid-hash").Returns(validRecord);
        users.FindById("user-1").Returns(user);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("valid-token");

        result.ReuseDetected.Should().BeFalse();
        result.Tokens.Should().NotBeNull();
        result.Tokens!.RefreshToken.Should().Be("new-raw-token");
        result.Tokens.AccessToken.Should().Be("new-access-token");
        validRecord.RevokedAt.Should().NotBeNull();
        validRecord.ReplacedByTokenHash.Should().Be("new-hash");
        store.Received(1).Add(Arg.Is<RefreshTokenRecord>(record =>
            record.FamilyId == familyId && record.TokenHash == "new-hash"));
    }

    [Fact]
    public void Refresh_WithRevokedTokenThatWasNotReplaced_ReturnsFailureWithoutReuseFlag()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        tokens.HashRefreshToken("logged-out-token").Returns("logged-out-hash");
        var loggedOutRecord = new RefreshTokenRecord
        {
            TokenHash = "logged-out-hash",
            UserId = "user-1",
            FamilyId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow,
            ReplacedByTokenHash = null
        };
        store.FindByHash("logged-out-hash").Returns(loggedOutRecord);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("logged-out-token");

        result.Tokens.Should().BeNull();
        result.ReuseDetected.Should().BeFalse();
    }

    [Fact]
    public void Refresh_WithReplacedToken_DetectsReuseAndRevokesEveryActiveTokenInFamily()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        var familyId = Guid.NewGuid();
        tokens.HashRefreshToken("stolen-token").Returns("stolen-hash");
        var replacedRecord = new RefreshTokenRecord
        {
            TokenHash = "stolen-hash",
            UserId = "user-1",
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReplacedByTokenHash = "some-newer-hash"
        };
        var stillActiveSibling = new RefreshTokenRecord
        {
            TokenHash = "some-newer-hash",
            UserId = "user-1",
            FamilyId = familyId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        store.FindByHash("stolen-hash").Returns(replacedRecord);
        store.ActiveFamilyTokens(familyId).Returns([stillActiveSibling]);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("stolen-token");

        result.Tokens.Should().BeNull();
        result.ReuseDetected.Should().BeTrue();
        stillActiveSibling.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void Refresh_WhenUserNoLongerExists_ReturnsFailure()
    {
        var store = Substitute.For<IRefreshTokenStore>();
        var users = Substitute.For<IUserStore>();
        var tokens = Substitute.For<ITokenService>();
        tokens.HashRefreshToken("orphaned-token").Returns("orphaned-hash");
        var orphanedRecord = new RefreshTokenRecord
        {
            TokenHash = "orphaned-hash",
            UserId = "deleted-user",
            FamilyId = Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        store.FindByHash("orphaned-hash").Returns(orphanedRecord);
        users.FindById("deleted-user").Returns((User?)null);
        var service = new RefreshTokenService(store, users, tokens);

        var result = service.Refresh("orphaned-token");

        result.Tokens.Should().BeNull();
        result.ReuseDetected.Should().BeFalse();
    }
}
