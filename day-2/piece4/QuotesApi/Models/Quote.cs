namespace QuotesApi.Models;

public sealed class Quote
{
    private Quote()
    {
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public static QuoteCreationResult Create(string? author, string? text)
    {
        if (string.IsNullOrWhiteSpace(author))
            return QuoteCreationResult.Failure("Author is required.");

        if (author.Trim().Length > 200)
            return QuoteCreationResult.Failure("Author must be 200 characters or fewer.");

        if (string.IsNullOrWhiteSpace(text))
            return QuoteCreationResult.Failure("Text is required.");

        if (text.Trim().Length > 1000)
            return QuoteCreationResult.Failure("Text must be 1000 characters or fewer.");

        return QuoteCreationResult.Success(new Quote(author.Trim(), text.Trim()));
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
