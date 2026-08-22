using CqrsLiteApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace CqrsLiteApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Quote> Quotes => Set<Quote>();
}
