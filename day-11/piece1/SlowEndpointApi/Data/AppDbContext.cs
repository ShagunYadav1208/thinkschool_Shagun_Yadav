using Microsoft.EntityFrameworkCore;
using SlowEndpointApi.Models;

namespace SlowEndpointApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Quote> Quotes => Set<Quote>();

    // No index is declared here on purpose - Quotes.AuthorId has no EF Core
    // relationship metadata (see Quote.cs), so there is nothing for EF Core's
    // "index every foreign key" convention to act on. The index used in the
    // comparison run is added later as a plain DDL statement against the
    // already-running database (via /admin/create-index below), exactly like
    // a DBA would add a missing index without redeploying the app.
}
