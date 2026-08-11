namespace Collections.Domain;

public sealed class Collection
{
    private const int MaximumNameLength = 80;
    private const int MaximumItems = 50;
    private readonly List<int> quoteIds = [];

    public Collection(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Collection name is required.");

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaximumNameLength)
            throw new DomainException("Collection name cannot exceed 80 characters.");

        Name = trimmedName;
    }

    public string Name { get; }
    public IReadOnlyCollection<int> QuoteIds => quoteIds.AsReadOnly();

    public void AddQuote(int quoteId)
    {
        if (quoteIds.Count == MaximumItems)
            throw new DomainException("A collection cannot contain more than 50 quotes.");

        if (quoteIds.Contains(quoteId))
            throw new DomainException("A quote can only appear once in a collection.");

        quoteIds.Add(quoteId);
    }

    public void RemoveQuote(int quoteId)
    {
        if (!quoteIds.Remove(quoteId))
            throw new DomainException("The quote is not in this collection.");
    }
}
