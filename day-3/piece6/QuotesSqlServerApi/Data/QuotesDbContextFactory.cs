using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuotesSqlServerApi.Data;

public sealed class QuotesDbContextFactory : IDesignTimeDbContextFactory<QuotesDbContext>
{
    public QuotesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=QuotesSqlServerApi;User Id=sa;Password=LocalDev@Passw0rd;TrustServerCertificate=True");

        return new QuotesDbContext(optionsBuilder.Options);
    }
}
