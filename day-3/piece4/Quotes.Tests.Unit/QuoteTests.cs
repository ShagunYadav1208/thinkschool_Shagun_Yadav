using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidAuthorAndText_ReturnsSuccess()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = Quote.Create("Rumi", "The wound is where the light enters.", createdAt);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Author.Should().Be("Rumi");
        result.Value.Text.Should().Be("The wound is where the light enters.");
        result.Value.CreatedAt.Should().Be(createdAt);
        result.Value.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_TrimsWhitespaceFromAuthorAndText()
    {
        var result = Quote.Create("  Rumi  ", "  Some text.  ", DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Author.Should().Be("Rumi");
        result.Value.Text.Should().Be("Some text.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceAuthor_ReturnsFailureWithAuthorError(string? author)
    {
        var result = Quote.Create(author, "Valid text", DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Field == "author");
    }

    [Fact]
    public void Create_WithAuthorExceedingMaximumLength_ReturnsFailureWithAuthorError()
    {
        var tooLongAuthor = new string('a', Quote.MaxAuthorLength + 1);

        var result = Quote.Create(tooLongAuthor, "Valid text", DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Field == "author");
    }

    [Fact]
    public void Create_WithAuthorAtMaximumLength_ReturnsSuccess()
    {
        var boundaryAuthor = new string('a', Quote.MaxAuthorLength);

        var result = Quote.Create(boundaryAuthor, "Valid text", DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Author.Should().HaveLength(Quote.MaxAuthorLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithNullOrWhitespaceText_ReturnsFailureWithTextError(string? text)
    {
        var result = Quote.Create("Valid author", text, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Field == "text");
    }

    [Fact]
    public void Create_WithTextExceedingMaximumLength_ReturnsFailureWithTextError()
    {
        var tooLongText = new string('a', Quote.MaxTextLength + 1);

        var result = Quote.Create("Valid author", tooLongText, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Field == "text");
    }

    [Fact]
    public void Create_WithTextAtMaximumLength_ReturnsSuccess()
    {
        var boundaryText = new string('a', Quote.MaxTextLength);

        var result = Quote.Create("Valid author", boundaryText, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Text.Should().HaveLength(Quote.MaxTextLength);
    }

    [Fact]
    public void Create_WithBothAuthorAndTextInvalid_ReturnsFailureWithBothErrors()
    {
        var result = Quote.Create(string.Empty, string.Empty, DateTimeOffset.UtcNow);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().Contain(error => error.Field == "author");
        result.Errors.Should().Contain(error => error.Field == "text");
    }

    [Fact]
    public void Delete_WhenNotYetDeleted_MarksQuoteAsDeleted()
    {
        var quote = Quote.Create("Rumi", "Some text", DateTimeOffset.UtcNow).Value!;

        quote.Delete();

        quote.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_ThrowsQuoteDomainException()
    {
        var quote = Quote.Create("Rumi", "Some text", DateTimeOffset.UtcNow).Value!;
        quote.Delete();

        var act = () => quote.Delete();

        act.Should().Throw<QuoteDomainException>()
            .WithMessage("Quote is already deleted.");
    }
}
