using QuotesApi.Models;
using QuotesApi.Data;
using QuotesApi.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace QuotesApi.Tests;

public class CollectionTests
{
    [Fact]
    public void AddItem_WhenQuoteAlreadyExists_ThrowsDomainException()
    {
        var collection = Collection.Create("Favourites", "user-1");
        collection.AddItem(42);

        var exception = Assert.Throws<CollectionDomainException>(() => collection.AddItem(42));

        Assert.Equal("This quote is already in the collection.", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void Create_WhenNameIsInvalid_ThrowsDomainException(string name)
    {
        Assert.Throws<CollectionDomainException>(() => Collection.Create(name, "user-1"));
    }

    [Fact]
    public void AddItem_WhenCollectionAlreadyHasFiftyItems_ThrowsDomainException()
    {
        var collection = Collection.Create("Favourites", "user-1");

        for (var quoteId = 1; quoteId <= 50; quoteId++)
        {
            collection.AddItem(quoteId);
        }

        var exception = Assert.Throws<CollectionDomainException>(() => collection.AddItem(51));

        Assert.Equal("A collection cannot contain more than 50 items.", exception.Message);
    }

    [Fact]
    public void RemoveItem_RemovesTheValueObjectFromTheAggregate()
    {
        var collection = Collection.Create("Favourites", "user-1");
        collection.AddItem(42);
        collection.RemoveItem(42);

        Assert.Empty(collection.Items);
    }

    [Fact]
    public async Task Repository_RoundTripsCollectionItemsAsOwnedValueObjects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setupDb = new QuotesDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
        }

        int collectionId;
        await using (var writeDb = new QuotesDbContext(options))
        {
            var repository = new CollectionRepository(writeDb);
            var collection = Collection.Create("Favourites", "user-1");
            collection.AddItem(42);

            await repository.AddAsync(collection, CancellationToken.None);
            collectionId = collection.Id;
        }

        await using (var readDb = new QuotesDbContext(options))
        {
            var repository = new CollectionRepository(readDb);
            var collection = await repository.GetByIdAsync(collectionId, CancellationToken.None);

            Assert.NotNull(collection);
            var item = Assert.Single(collection.Items);
            Assert.Equal(42, item.QuoteId);
            Assert.True(item.AddedAt <= DateTimeOffset.UtcNow);
        }
    }
}
