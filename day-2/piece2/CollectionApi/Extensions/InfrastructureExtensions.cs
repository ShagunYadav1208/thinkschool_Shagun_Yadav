using CollectionApi.Data;
using CollectionApi.Repositories;
using CollectionApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddCollectionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<CollectionsDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Collections") ?? "Data Source=collections.db"));

        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddTransient<ICollectionService, CollectionService>();

        return services;
    }
}
