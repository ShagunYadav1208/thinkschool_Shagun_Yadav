using QuotesApi.Models;
using Xunit;

namespace QuotesApi.Tests.Models;

public class QuoteTests
{
    [Fact]
    public void Create_WithValidAuthorAndText_Succeeds()
    {
        var result = Quote.Create("Rumi", "The wound is where the light enters.", DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("Rumi", result.Value!.Author);
    }

    [Fact]
    public void Create_WithEmptyAuthor_ReturnsFailure()
    {
        var result = Quote.Create("   ", "Some text", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Field == "author");
    }

    [Fact]
    public void Create_WithAuthorLongerThan200Characters_ReturnsFailure()
    {
        var result = Quote.Create(new string('a', 201), "Some text", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Field == "author");
    }

    [Fact]
    public void Create_WithEmptyText_ReturnsFailure()
    {
        var result = Quote.Create("Rumi", "   ", DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Field == "text");
    }

    [Fact]
    public void Create_WithTextLongerThan1000Characters_ReturnsFailure()
    {
        var result = Quote.Create("Rumi", new string('a', 1001), DateTimeOffset.UtcNow);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Field == "text");
    }

    [Fact]
    public void Delete_MarksTheQuoteAsDeleted()
    {
        var quote = Quote.Create("Rumi", "Some text", DateTimeOffset.UtcNow).Value!;

        quote.Delete();

        Assert.True(quote.IsDeleted);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_Throws()
    {
        var quote = Quote.Create("Rumi", "Some text", DateTimeOffset.UtcNow).Value!;
        quote.Delete();

        var act = () => quote.Delete();

        Assert.Throws<QuoteDomainException>(act);
    }
}
