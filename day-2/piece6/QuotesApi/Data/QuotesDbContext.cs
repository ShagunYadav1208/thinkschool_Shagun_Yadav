using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(user => user.Email).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(token => token.Token).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(token => token.FamilyId);
    }
}
