using Microsoft.EntityFrameworkCore;

namespace QueryTranslationDemo;

public class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Quote> Quotes => Set<Quote>();
}

public static class QuotesDbContextFactory
{
    /// <summary>
    /// Builds a context wired to log ONLY the SQL EF Core actually sends to SQLite
    /// (Microsoft.EntityFrameworkCore.Database.Command category), with
    /// EnableSensitiveDataLogging() on so parameter VALUES show up in the log too -
    /// dev-only, never do this against a database with real user data.
    /// </summary>
    public static QuotesDbContext Create(string connectionString, Action<string> logSink)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>()
            .UseSqlite(connectionString)
            .LogTo(
                logSink,
                new[] { Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted },
                Microsoft.Extensions.Logging.LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;
        return new QuotesDbContext(options);
    }
}
