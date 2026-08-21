namespace FixedEndpointApi.Models;

public class Author
{
    public int AuthorId { get; set; }
    public string Name { get; set; } = string.Empty;

    // A real navigation property this time - piece1's Quote.AuthorId was a bare
    // int with no relationship metadata (the actual cause of the missing index).
    // Fixing the anti-pattern properly means fixing the model, not just the query.
    public ICollection<Quote> Quotes { get; set; } = new List<Quote>();
}
