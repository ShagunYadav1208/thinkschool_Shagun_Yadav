using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public sealed class QuoteExpiryServiceTests
{
    [Fact]
    public void GetAge_ClockNowAfterCreation_ReturnsElapsedTime()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(createdAt.AddMinutes(30));
        var service = new QuoteExpiryService(clock);
        var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

        var age = service.GetAge(quote);

        age.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void GetAge_ClockBeforeCreation_ReturnsZero()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(createdAt.AddMinutes(-5));
        var service = new QuoteExpiryService(clock);
        var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

        var age = service.GetAge(quote);

        age.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void IsExpired_AgeEqualToTtl_ReturnsTrue()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(createdAt.AddHours(1));
        var service = new QuoteExpiryService(clock);
        var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

        var isExpired = service.IsExpired(quote, TimeSpan.FromHours(1));

        isExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_AgeBelowTtl_ReturnsFalse()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(createdAt.AddMinutes(59));
        var service = new QuoteExpiryService(clock);
        var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

        var isExpired = service.IsExpired(quote, TimeSpan.FromHours(1));

        isExpired.Should().BeFalse();
    }

    [Fact]
    public void IsExpired_NonPositiveTtl_ReturnsTrue()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(createdAt);
        var service = new QuoteExpiryService(clock);
        var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

        var isExpired = service.IsExpired(quote, TimeSpan.Zero);

        isExpired.Should().BeTrue();
    }
}
