using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ParkFlow.Modules.Identity.Application.Identity;

namespace ParkFlow.Modules.Identity.Application;

using Domain = ParkFlow.Modules.Identity.Domain.User;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services) =>
        services
            .AddSingleton<IPasswordHasher<Domain>, PasswordHasher<Domain>>()
            .AddScoped<IdentityApplicationService>();
}
