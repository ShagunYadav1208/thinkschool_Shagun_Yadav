using Collections.Domain;
using FluentAssertions;

namespace Tests.Domain;

public class CollectionTests
{
    [Fact]
    public void CreatingWithAnEmptyName_Throws()
    {
        var act = () => new Collection(" ");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CreatingWithANameLongerThan80Characters_Throws()
    {
        var act = () => new Collection(new string('a', 81));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddingThe51stQuote_Throws()
    {
        var collection = new Collection("Favourites");
        Enumerable.Range(1, 50).ToList().ForEach(collection.AddQuote);
        var act = () => collection.AddQuote(51);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddingADuplicateQuoteId_Throws()
    {
        var collection = new Collection("Favourites");
        collection.AddQuote(1);
        var act = () => collection.AddQuote(1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RemovingANonExistentQuote_Throws()
    {
        var collection = new Collection("Favourites");
        var act = () => collection.RemoveQuote(1);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddingThenRemovingAQuote_LeavesNoItems()
    {
        var collection = new Collection("Favourites");
        collection.AddQuote(1);
        collection.RemoveQuote(1);
        collection.QuoteIds.Should().BeEmpty();
    }
}
