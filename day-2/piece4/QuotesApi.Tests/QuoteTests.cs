using FluentAssertions;
using QuotesApi.Models;

namespace QuotesApi.Tests;

public class QuoteTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithMissingAuthor_ReturnsDomainError(string? author)
    {
        var result = Quote.Create(author, "A valid quote");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Author is required.");
    }

    [Fact]
    public void Create_WithTextOver1000Characters_ReturnsDomainError()
    {
        var result = Quote.Create("Author", new string('x', 1001));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Text must be 1000 characters or fewer.");
    }

    [Fact]
    public void Create_WithValidValues_ReturnsAnImmutableTextQuote()
    {
        var result = Quote.Create("  Maya Angelou  ", "  Nothing will work unless you do.  ");
        result.IsSuccess.Should().BeTrue();
        result.Quote!.Author.Should().Be("Maya Angelou");
        result.Quote.Text.Should().Be("Nothing will work unless you do.");
    }

    [Fact]
    public void SoftDelete_MarksTheQuoteAsDeleted()
    {
        var quote = Quote.Create("Author", "Text").Quote!;
        quote.SoftDelete();
        quote.IsDeleted.Should().BeTrue();
    }
}
