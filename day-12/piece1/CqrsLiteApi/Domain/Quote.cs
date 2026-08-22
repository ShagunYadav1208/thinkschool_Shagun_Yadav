namespace CqrsLiteApi.Domain;

public class Quote
{
    public int QuoteId { get; set; }
    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
