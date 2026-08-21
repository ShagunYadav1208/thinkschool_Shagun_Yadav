namespace SlowEndpointApi.Models;

public class Quote
{
    public int QuoteId { get; set; }

    // Deliberately a plain int, with NO EF Core relationship configured
    // (no navigation property, no HasOne/WithMany in OnModelCreating).
    // This is the realistic way a "missing index" actually happens: EF Core
    // auto-creates an index on every foreign key it knows about, but it only
    // knows about a foreign key if you tell it one exists. A column that
    // merely LOOKS like a foreign key - same name, same values - gets none
    // of that for free.
    public int AuthorId { get; set; }

    public string QuoteText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
