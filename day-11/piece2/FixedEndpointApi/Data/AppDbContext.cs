using Microsoft.EntityFrameworkCore;
using FixedEndpointApi.Models;

namespace FixedEndpointApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Quote> Quotes => Set<Quote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A real relationship this time - EF Core's "index every foreign key"
        // convention now applies, so EnsureCreated() creates IX_Quotes_AuthorId
        // automatically. The /admin endpoints below still exist so the before
        // (no index) and after (indexed) phases can be profiled from the same
        // running app, same as day-11/piece1.
        modelBuilder.Entity<Quote>()
            .HasOne(q => q.Author)
            .WithMany(a => a.Quotes)
            .HasForeignKey(q => q.AuthorId);
    }
}
