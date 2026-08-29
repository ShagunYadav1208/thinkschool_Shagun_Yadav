using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    /// <summary>
    /// Provider choice is driven entirely by the connection string's shape, not an
    /// explicit environment check: an Azure SQL connection string contains
    /// "Authentication=Active Directory Managed Identity" and no password anywhere -
    /// when the app is running as an Azure App Service, Microsoft.Data.SqlClient
    /// exchanges that for a token via the App Service's system-assigned managed
    /// identity automatically. Locally, the SQLite fallback ("Data Source=quotes.db")
    /// needs nothing extra. Either way, no credential is ever read from config,
    /// checked into source, or set as an App Service secret.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=quotes.db";

        var usesAzureSql = connectionString.Contains("Authentication=Active Directory", StringComparison.OrdinalIgnoreCase);

        services.AddDbContext<QuotesDbContext>(options =>
        {
            if (usesAzureSql)
                options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("QuotesApi"));
            else
                options.UseSqlite(connectionString);
        });

        var allowedOrigin = configuration["Cors:AllowedOrigin"];
        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                if (!string.IsNullOrWhiteSpace(allowedOrigin))
                    policy.WithOrigins(allowedOrigin).AllowAnyHeader().AllowAnyMethod();
            });
        });

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        return services;
    }
}