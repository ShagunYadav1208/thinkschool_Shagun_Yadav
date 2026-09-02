using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Explicit, not left to bare convention: one Quote -> many OutboxMessage, required (an
        // outbox row always describes a real quote), cascading delete (an outbox row for a quote
        // that no longer exists is meaningless, not an orphan worth keeping).
        modelBuilder.Entity<OutboxMessage>()
            .HasOne(message => message.Quote)
            .WithMany(quote => quote.OutboxMessages)
            .HasForeignKey(message => message.QuoteId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}