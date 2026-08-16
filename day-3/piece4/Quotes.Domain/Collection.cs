namespace Quotes.Domain;

public sealed class Collection
{
    public const int MinimumNameLength = 3;
    public const int MaximumNameLength = 80;
    public const int MaximumItems = 50;

    private readonly List<int> quoteIds = [];

    public Collection(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CollectionDomainException("Collection name is required.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length is < MinimumNameLength or > MaximumNameLength)
        {
            throw new CollectionDomainException(
                $"Collection name must be between {MinimumNameLength} and {MaximumNameLength} characters.");
        }

        Name = trimmedName;
    }

    public string Name { get; }

    public IReadOnlyCollection<int> QuoteIds => quoteIds.AsReadOnly();

    public void AddQuote(int quoteId)
    {
        if (quoteIds.Count >= MaximumItems)
        {
            throw new CollectionDomainException("A collection cannot contain more than 50 quotes.");
        }

        if (quoteIds.Contains(quoteId))
        {
            throw new CollectionDomainException("A quote can only appear once in a collection.");
        }

        quoteIds.Add(quoteId);
    }

    public void RemoveQuote(int quoteId)
    {
        if (!quoteIds.Remove(quoteId))
        {
            throw new CollectionDomainException("The quote is not in this collection.");
        }
    }
}
