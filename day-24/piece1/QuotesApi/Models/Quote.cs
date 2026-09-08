using System.Text.Json.Serialization;

namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    // Day 20: one quote can (in principle) have more than one outbox row against it - the EF Core
    // relationship this exercise asks for. In practice this app only ever writes one per quote
    // (on creation), but the FK/navigation is real, not decorative: QuotesDbContext's
    // OnModelCreating configures it explicitly, and deleting a quote cascades to its outbox rows.
    //
    // [JsonIgnore] is load-bearing, not cosmetic: after QuoteEndpointExtensions' POST handler adds
    // an OutboxMessage in the same DbContext, EF's change tracker fixes up BOTH navigation
    // directions automatically (this collection, and OutboxMessage.Quote pointing back here) - a
    // real Quote -> OutboxMessage -> Quote -> ... cycle that crashed System.Text.Json with a 500 on
    // the very first POST /api/quotes/ until this was added. Confirmed live, not guessed.
    [JsonIgnore]
    public ICollection<OutboxMessage> OutboxMessages { get; set; } = [];
}