using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Payment.Application.Payments;

namespace ParkFlow.Modules.Payment.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services) =>
        services.AddScoped<PaymentApplicationService>();
}
