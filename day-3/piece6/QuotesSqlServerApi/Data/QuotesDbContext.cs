using Microsoft.EntityFrameworkCore;
using QuotesSqlServerApi.Models;

namespace QuotesSqlServerApi.Data;

public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.Property(q => q.Author).HasMaxLength(100).IsRequired();
            entity.Property(q => q.Text).HasMaxLength(1000).IsRequired();
        });
    }
}
