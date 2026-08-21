namespace FixedEndpointApi.Models;

public class Quote
{
    public int QuoteId { get; set; }

    // Now a real foreign key, wired up via Fluent API in AppDbContext -
    // this is what lets EF Core's "index every FK" convention actually see
    // AuthorId as one and create IX_Quotes_AuthorId automatically.
    public int AuthorId { get; set; }
    public Author Author { get; set; } = null!;

    public string QuoteText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
