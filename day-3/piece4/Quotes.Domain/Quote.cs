namespace Quotes.Domain;

public sealed class Quote
{
    public const int MinAuthorLength = 1;
    public const int MaxAuthorLength = 200;
    public const int MinTextLength = 1;
    public const int MaxTextLength = 1000;

    private Quote()
    {
    }

    private Quote(string author, string text, DateTimeOffset createdAt)
    {
        Author = author;
        Text = text;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string Author { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public static Result<Quote> Create(string? author, string? text, DateTimeOffset createdAt)
    {
        var errors = new List<QuoteError>();

        var trimmedAuthor = author?.Trim() ?? string.Empty;
        if (trimmedAuthor.Length is < MinAuthorLength or > MaxAuthorLength)
        {
            errors.Add(new QuoteError(
                "author",
                $"Author must be between {MinAuthorLength} and {MaxAuthorLength} characters."));
        }

        var trimmedText = text?.Trim() ?? string.Empty;
        if (trimmedText.Length is < MinTextLength or > MaxTextLength)
        {
            errors.Add(new QuoteError(
                "text",
                $"Text must be between {MinTextLength} and {MaxTextLength} characters."));
        }

        return errors.Count > 0
            ? Result<Quote>.Failure(errors)
            : Result<Quote>.Success(new Quote(trimmedAuthor, trimmedText, createdAt));
    }

    public void Delete()
    {
        if (IsDeleted)
        {
            throw new QuoteDomainException("Quote is already deleted.");
        }

        IsDeleted = true;
    }
}
