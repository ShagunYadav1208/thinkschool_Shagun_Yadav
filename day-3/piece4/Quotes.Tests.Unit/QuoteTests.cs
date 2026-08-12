using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public sealed class QuoteTests
{
    [Fact]
    public void Create_ValidInput_ReturnsTrimmedQuote()
    {
        var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

        var quote = Quote.Create(" Shagun ", " Testing gives confidence. ", createdAt, [" API ", "api", "Auth"]);

        quote.Author.Should().Be("Shagun");
        quote.Text.Should().Be("Testing gives confidence.");
        quote.CreatedAt.Should().Be(createdAt);
        quote.Tags.Should().Equal("api", "auth");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankAuthor_ThrowsDomainException(string? author)
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create(author, "Testing gives confidence.", createdAt);

        act.Should().Throw<DomainException>().WithMessage("Author is required.");
    }

    [Fact]
    public void Create_AuthorLongerThanMaximum_ThrowsDomainException()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create(new string('a', 81), "Testing gives confidence.", createdAt);

        act.Should().Throw<DomainException>().WithMessage("Author cannot exceed 80 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_ThrowsDomainException(string? text)
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create("Shagun", text, createdAt);

        act.Should().Throw<DomainException>().WithMessage("Text is required.");
    }

    [Fact]
    public void Create_TextShorterThanMinimum_ThrowsDomainException()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create("Shagun", "too short", createdAt);

        act.Should().Throw<DomainException>().WithMessage("Text must be at least 10 characters.");
    }

    [Fact]
    public void Create_TextLongerThanMaximum_ThrowsDomainException()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create("Shagun", new string('q', 281), createdAt);

        act.Should().Throw<DomainException>().WithMessage("Text cannot exceed 280 characters.");
    }

    [Fact]
    public void Create_DefaultCreatedAt_ThrowsDomainException()
    {
        Action act = () => Quote.Create("Shagun", "Testing gives confidence.", default);

        act.Should().Throw<DomainException>().WithMessage("CreatedAt is required.");
    }

    [Fact]
    public void Create_BlankTag_ThrowsDomainException()
    {
        var createdAt = DateTimeOffset.UtcNow;

        Action act = () => Quote.Create("Shagun", "Testing gives confidence.", createdAt, ["api", " "]);

        act.Should().Throw<DomainException>().WithMessage("Tags cannot be blank.");
    }
}
