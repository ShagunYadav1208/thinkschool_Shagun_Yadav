using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerBenchmark;

public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
}
