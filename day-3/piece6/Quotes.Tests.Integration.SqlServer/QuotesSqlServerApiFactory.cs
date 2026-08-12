using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesSqlServerApi.Abstractions;
using QuotesSqlServerApi.Data;

namespace Quotes.Tests.Integration.SqlServer;

public sealed class QuotesSqlServerApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IClock>();

            services.AddDbContext<QuotesDbContext>(options => options.UseSqlServer(connectionString));
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        await db.Database.MigrateAsync();
    }
}
