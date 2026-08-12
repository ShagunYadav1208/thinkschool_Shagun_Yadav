namespace Quotes.Domain;

public sealed record Quote
{
    private Quote(string author, string text, DateTimeOffset createdAt, string[] tags)
    {
        Author = author;
        Text = text;
        CreatedAt = createdAt;
        Tags = tags;
    }

    public string Author { get; }
    public string Text { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyCollection<string> Tags { get; }

    public static Quote Create(string? author, string? text, DateTimeOffset createdAt, string[]? tags = null)
    {
        var validation = new CreateQuoteRequestValidator().Validate(new CreateQuoteRequest(author, text, tags));
        if (!validation.IsValid)
        {
            throw new DomainException(validation.Errors.First().Message);
        }

        if (createdAt == default)
        {
            throw new DomainException("CreatedAt is required.");
        }

        var normalizedTags = (tags ?? [])
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Quote(author!.Trim(), text!.Trim(), createdAt, normalizedTags);
    }
}
