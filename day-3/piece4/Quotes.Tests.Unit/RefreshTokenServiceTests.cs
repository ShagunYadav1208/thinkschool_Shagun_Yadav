using FluentAssertions;
using NSubstitute;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public void Issue_NewUser_ReturnsAccessAndRefreshTokens()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);

        var pair = service.Issue("user-123");

        pair.AccessToken.Should().StartWith("access-token-for-user-123");
        pair.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Refresh_ActiveRefreshToken_RotatesRefreshToken()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);
        var firstPair = service.Issue("user-123");

        var result = service.Refresh(firstPair.RefreshToken);

        result.IsSuccess.Should().BeTrue();
        result.ReuseDetected.Should().BeFalse();
        result.Tokens!.RefreshToken.Should().NotBe(firstPair.RefreshToken);
    }

    [Fact]
    public void Refresh_UnknownRefreshToken_ReturnsFailure()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);

        var result = service.Refresh("not-a-real-refresh-token");

        result.IsSuccess.Should().BeFalse();
        result.ReuseDetected.Should().BeFalse();
    }

    [Fact]
    public void Refresh_ExpiredRefreshToken_ReturnsFailure()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);
        var pair = service.Issue("user-123");
        clock.UtcNow = clock.UtcNow.AddDays(8);

        var result = service.Refresh(pair.RefreshToken);

        result.IsSuccess.Should().BeFalse();
        result.ReuseDetected.Should().BeFalse();
    }

    [Fact]
    public void Refresh_ReusedRotatedToken_ReturnsReuseDetected()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);
        var original = service.Issue("user-123");
        service.Refresh(original.RefreshToken);

        var result = service.Refresh(original.RefreshToken);

        result.IsSuccess.Should().BeFalse();
        result.ReuseDetected.Should().BeTrue();
    }

    [Fact]
    public void Refresh_ReusedRotatedToken_NotifiesReuseDetector()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);
        var original = service.Issue("user-123");
        service.Refresh(original.RefreshToken);

        service.Refresh(original.RefreshToken);

        reuseNotifier.Received(1).ReuseDetected(Arg.Any<Guid>(), "user-123");
    }

    [Fact]
    public void Refresh_ReusedRotatedToken_RevokesReplacementToken()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
        var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
        var service = new RefreshTokenService(clock, reuseNotifier);
        var original = service.Issue("user-123");
        var rotated = service.Refresh(original.RefreshToken).Tokens!;
        service.Refresh(original.RefreshToken);

        var result = service.Refresh(rotated.RefreshToken);

        result.IsSuccess.Should().BeFalse();
        result.ReuseDetected.Should().BeFalse();
    }
}
