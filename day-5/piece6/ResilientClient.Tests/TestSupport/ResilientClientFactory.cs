using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ResilientClient.Tests.TestSupport;

/// <summary>
/// Runs the real app (real Program.cs, real AddResilienceHandler pipeline) with only the
/// innermost HttpMessageHandler swapped for <see cref="FakeSequenceHandler"/> — everything the
/// exercise asks about (retry, circuit breaker, timeout, logging) runs exactly as configured in
/// production; only the network call itself is faked.
/// </summary>
public sealed class ResilientClientFactory(FakeSequenceHandler downstreamHandler) : WebApplicationFactory<Program>
{
    public ListLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient("downstream-service")
                .ConfigurePrimaryHttpMessageHandler(() => downstreamHandler);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(Logs);
            logging.SetMinimumLevel(LogLevel.Information);
        });
    }
}
