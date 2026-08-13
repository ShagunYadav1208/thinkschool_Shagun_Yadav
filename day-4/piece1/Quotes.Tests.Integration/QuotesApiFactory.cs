using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesIntegrationApi.Abstractions;
using QuotesIntegrationApi.Data;

namespace Quotes.Tests.Integration;

public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IClock>();

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<QuotesDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}
