using FluentAssertions;
using NSubstitute;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public class QuoteServiceTests
{
    [Fact]
    public async Task CreateAsync_WithValidRequest_UsesClockForCreatedAt()
    {
        var repository = Substitute.For<IQuoteRepository>();
        repository.AddAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Quote>()));
        var fixedTime = new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.Zero);
        var clock = new FakeClock(fixedTime);
        var service = new QuoteService(repository, clock);

        var created = await service.CreateAsync("Maya Angelou", "Nothing will work unless you do.", CancellationToken.None);

        created.CreatedAt.Should().Be(fixedTime);
    }

    [Fact]
    public async Task CreateAsync_WithValidRequest_CallsRepositoryAddAsyncWithTrimmedValues()
    {
        var repository = Substitute.For<IQuoteRepository>();
        repository.AddAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<Quote>()));
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new QuoteService(repository, clock);

        var created = await service.CreateAsync("  Maya Angelou  ", "  Nothing will work unless you do.  ", CancellationToken.None);

        created.Author.Should().Be("Maya Angelou");
        created.Text.Should().Be("Nothing will work unless you do.");
        await repository.Received(1).AddAsync(Arg.Is<Quote>(quote => quote.Author == "Maya Angelou"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithInvalidAuthor_ThrowsAndNeverCallsRepository()
    {
        var repository = Substitute.For<IQuoteRepository>();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new QuoteService(repository, clock);

        var act = async () => await service.CreateAsync(null, "Valid text", CancellationToken.None);

        await act.Should().ThrowAsync<QuoteValidationException>();
        await repository.DidNotReceive().AddAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WithInvalidText_ThrowsAndNeverCallsRepository()
    {
        var repository = Substitute.For<IQuoteRepository>();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new QuoteService(repository, clock);

        var act = async () => await service.CreateAsync("Valid author", null, CancellationToken.None);

        await act.Should().ThrowAsync<QuoteValidationException>();
        await repository.DidNotReceive().AddAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenQuoteExists_MarksDeletedAndPersistsThroughRepository()
    {
        var repository = Substitute.For<IQuoteRepository>();
        var existingQuote = Quote.Create("Rumi", "Some text", DateTimeOffset.UtcNow).Value!;
        repository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(existingQuote);
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new QuoteService(repository, clock);

        var deleted = await service.DeleteAsync(1, CancellationToken.None);

        deleted.Should().BeTrue();
        existingQuote.IsDeleted.Should().BeTrue();
        await repository.Received(1).UpdateAsync(existingQuote, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WhenQuoteDoesNotExist_ReturnsFalseWithoutCallingUpdate()
    {
        var repository = Substitute.For<IQuoteRepository>();
        repository.GetByIdAsync(999, Arg.Any<CancellationToken>()).Returns((Quote?)null);
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var service = new QuoteService(repository, clock);

        var deleted = await service.DeleteAsync(999, CancellationToken.None);

        deleted.Should().BeFalse();
        await repository.DidNotReceive().UpdateAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>());
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
