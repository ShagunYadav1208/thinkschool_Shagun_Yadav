using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddQuoteInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Quotes") ?? "Data Source=quotes.db"));

        return services;
    }
}
