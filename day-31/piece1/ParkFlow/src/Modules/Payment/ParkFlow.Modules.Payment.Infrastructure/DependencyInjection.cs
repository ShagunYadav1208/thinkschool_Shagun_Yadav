using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Payment.Application.Abstractions;
using ParkFlow.Modules.Payment.Infrastructure.Outbox;
using ParkFlow.Modules.Payment.Infrastructure.Persistence;

namespace ParkFlow.Modules.Payment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<PaymentDbContext>(configureDb);
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher>();

        return services;
    }
}
