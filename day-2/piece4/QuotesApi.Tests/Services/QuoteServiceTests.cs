using QuotesApi.Models;
using QuotesApi.Repositories;
using QuotesApi.Services;
using Xunit;

namespace QuotesApi.Tests.Services;

public class QuoteServiceTests
{
    [Fact]
    public async Task CreateAsync_UsesTheTimeProvidedByTheClock()
    {
        var expectedTime = new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.Zero);
        var repository = new FakeQuoteRepository();
        var service = new QuoteService(repository, new FakeClock(expectedTime));

        var created = await service.CreateAsync(
            new CreateQuoteRequest
            {
                Author = "  Maya Angelou  ",
                Text = "  Nothing will work unless you do.  "
            },
            CancellationToken.None);

        Assert.Equal(expectedTime, created.CreatedAt);
        Assert.Equal("Maya Angelou", created.Author);
        Assert.Equal("Nothing will work unless you do.", created.Text);
        Assert.Same(created, repository.AddedQuote);
    }

    [Fact]
    public async Task CreateAsync_WithNullAuthor_ThrowsQuoteValidationException()
    {
        var repository = new FakeQuoteRepository();
        var service = new QuoteService(repository, new FakeClock(DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<QuoteValidationException>(
            () => service.CreateAsync(
                new CreateQuoteRequest { Author = null, Text = "Some text" },
                CancellationToken.None));

        Assert.Contains(exception.Errors, error => error.Field == "author");
        Assert.Null(repository.AddedQuote);
    }

    [Fact]
    public async Task DeleteAsync_WhenQuoteExists_MarksItDeletedAndPersists()
    {
        var existing = Quote.Create("Rumi", "The wound is where the light enters.", DateTimeOffset.UtcNow).Value!;
        var repository = new FakeQuoteRepository { ExistingQuote = existing };
        var service = new QuoteService(repository, new FakeClock(DateTimeOffset.UtcNow));

        var deleted = await service.DeleteAsync(1, CancellationToken.None);

        Assert.True(deleted);
        Assert.True(existing.IsDeleted);
        Assert.Same(existing, repository.UpdatedQuote);
    }

    [Fact]
    public async Task DeleteAsync_WhenQuoteDoesNotExist_ReturnsFalse()
    {
        var repository = new FakeQuoteRepository();
        var service = new QuoteService(repository, new FakeClock(DateTimeOffset.UtcNow));

        var deleted = await service.DeleteAsync(999, CancellationToken.None);

        Assert.False(deleted);
        Assert.Null(repository.UpdatedQuote);
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeQuoteRepository : IQuoteRepository
    {
        public Quote? AddedQuote { get; private set; }
        public Quote? UpdatedQuote { get; private set; }
        public Quote? ExistingQuote { get; set; }

        public Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
        {
            AddedQuote = quote;
            return Task.FromResult(quote);
        }

        public Task UpdateAsync(Quote quote, CancellationToken cancellationToken)
        {
            UpdatedQuote = quote;
            return Task.CompletedTask;
        }

        public Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(ExistingQuote);

        public Task<List<Quote>> GetPagedAsync(
            int page,
            int size,
            CancellationToken cancellationToken) =>
            Task.FromResult(new List<Quote>());
    }
}
