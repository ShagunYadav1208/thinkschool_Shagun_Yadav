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

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FakeQuoteRepository : IQuoteRepository
    {
        public Quote? AddedQuote { get; private set; }

        public Task<Quote> AddAsync(Quote quote, CancellationToken cancellationToken)
        {
            quote.Id = 1;
            AddedQuote = quote;
            return Task.FromResult(quote);
        }

        public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<Quote?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<Quote?>(null);

        public Task<List<Quote>> GetPagedAsync(
            int page,
            int size,
            CancellationToken cancellationToken) =>
            Task.FromResult(new List<Quote>());
    }
}
