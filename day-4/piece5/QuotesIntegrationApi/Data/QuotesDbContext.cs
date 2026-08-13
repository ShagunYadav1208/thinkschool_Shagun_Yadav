using Microsoft.EntityFrameworkCore;
using QuotesIntegrationApi.Models;

namespace QuotesIntegrationApi.Data;

public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
}
