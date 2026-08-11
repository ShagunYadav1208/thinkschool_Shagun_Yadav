namespace QuotesApi.Models;

public sealed class Collection
{
    private const int MaximumNameLength = 80;
    private const int MaximumItems = 50;
    private readonly List<CollectionItem> items = [];

    private Collection()
    {
    }

    private Collection(string name, string ownerId)
    {
        Name = name;
        OwnerId = ownerId;
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string OwnerId { get; private set; } = string.Empty;
    public IReadOnlyCollection<CollectionItem> Items => items.AsReadOnly();

    public static Collection Create(string? name, string? ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new CollectionDomainException("Collection name is required.");

        var trimmedName = name.Trim();
        if (trimmedName.Length is < 3 or > MaximumNameLength)
            throw new CollectionDomainException("Collection name must be between 3 and 80 characters.");

        if (string.IsNullOrWhiteSpace(ownerId))
            throw new CollectionDomainException("Owner ID is required.");

        return new Collection(trimmedName, ownerId.Trim());
    }

    public void AddItem(int quoteId)
    {
        if (items.Count >= MaximumItems)
            throw new CollectionDomainException("A collection cannot contain more than 50 items.");

        if (items.Any(item => item.QuoteId == quoteId))
            throw new CollectionDomainException("This quote is already in the collection.");

        items.Add(new CollectionItem(quoteId, DateTimeOffset.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = items.SingleOrDefault(item => item.QuoteId == quoteId);
        if (item is null)
            throw new CollectionDomainException("This quote is not in the collection.");

        items.Remove(item);
    }
}
