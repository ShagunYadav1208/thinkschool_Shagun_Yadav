using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public class CollectionTests
{
    [Fact]
    public void Constructor_WithValidName_CreatesCollection()
    {
        var collection = new Collection("Favourites");

        collection.Name.Should().Be("Favourites");
        collection.QuoteIds.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_TrimsWhitespaceFromName()
    {
        var collection = new Collection("  Favourites  ");

        collection.Name.Should().Be("Favourites");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithNullOrWhitespaceName_ThrowsCollectionDomainException(string? name)
    {
        var act = () => new Collection(name);

        act.Should().Throw<CollectionDomainException>()
            .WithMessage("Collection name is required.");
    }

    [Fact]
    public void Constructor_WithNameShorterThanMinimumLength_ThrowsCollectionDomainException()
    {
        var tooShortName = new string('a', Collection.MinimumNameLength - 1);

        var act = () => new Collection(tooShortName);

        act.Should().Throw<CollectionDomainException>();
    }

    [Fact]
    public void Constructor_WithNameAtMinimumLength_CreatesCollection()
    {
        var boundaryName = new string('a', Collection.MinimumNameLength);

        var collection = new Collection(boundaryName);

        collection.Name.Should().HaveLength(Collection.MinimumNameLength);
    }

    [Fact]
    public void Constructor_WithNameLongerThanMaximumLength_ThrowsCollectionDomainException()
    {
        var tooLongName = new string('a', Collection.MaximumNameLength + 1);

        var act = () => new Collection(tooLongName);

        act.Should().Throw<CollectionDomainException>();
    }

    [Fact]
    public void Constructor_WithNameAtMaximumLength_CreatesCollection()
    {
        var boundaryName = new string('a', Collection.MaximumNameLength);

        var collection = new Collection(boundaryName);

        collection.Name.Should().HaveLength(Collection.MaximumNameLength);
    }

    [Fact]
    public void AddQuote_WithNewQuoteId_AddsToCollection()
    {
        var collection = new Collection("Favourites");

        collection.AddQuote(42);

        collection.QuoteIds.Should().ContainSingle().Which.Should().Be(42);
    }

    [Fact]
    public void AddQuote_WithDuplicateQuoteId_ThrowsCollectionDomainException()
    {
        var collection = new Collection("Favourites");
        collection.AddQuote(42);

        var act = () => collection.AddQuote(42);

        act.Should().Throw<CollectionDomainException>()
            .WithMessage("A quote can only appear once in a collection.");
    }

    [Fact]
    public void AddQuote_WhenAtMaximumCapacity_ThrowsCollectionDomainException()
    {
        var collection = new Collection("Favourites");
        for (var quoteId = 1; quoteId <= Collection.MaximumItems; quoteId++)
        {
            collection.AddQuote(quoteId);
        }

        var act = () => collection.AddQuote(Collection.MaximumItems + 1);

        act.Should().Throw<CollectionDomainException>()
            .WithMessage("A collection cannot contain more than 50 quotes.");
    }

    [Fact]
    public void AddQuote_AtExactlyMaximumCapacity_Succeeds()
    {
        var collection = new Collection("Favourites");
        for (var quoteId = 1; quoteId < Collection.MaximumItems; quoteId++)
        {
            collection.AddQuote(quoteId);
        }

        collection.AddQuote(Collection.MaximumItems);

        collection.QuoteIds.Should().HaveCount(Collection.MaximumItems);
    }

    [Fact]
    public void RemoveQuote_WithExistingQuoteId_RemovesFromCollection()
    {
        var collection = new Collection("Favourites");
        collection.AddQuote(42);

        collection.RemoveQuote(42);

        collection.QuoteIds.Should().BeEmpty();
    }

    [Fact]
    public void RemoveQuote_WithNonExistentQuoteId_ThrowsCollectionDomainException()
    {
        var collection = new Collection("Favourites");

        var act = () => collection.RemoveQuote(42);

        act.Should().Throw<CollectionDomainException>()
            .WithMessage("The quote is not in this collection.");
    }
}
