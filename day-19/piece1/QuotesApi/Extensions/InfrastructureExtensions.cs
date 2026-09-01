using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using QuotesApi.BackgroundJobs;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.ServiceBusMessaging;

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
        IConfiguration configuration,
        IHostEnvironment environment)
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

        // Day 18: background jobs. The queue and job store are singletons (they must outlive
        // any one request); QueuedHostedService is the BackgroundService that drains the queue.
        // ShutdownTimeout gives that drain loop - and whatever job is running inside it - up to
        // 10s to unwind cleanly on Ctrl+C/SIGTERM before the host tears the process down anyway.
        services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(capacity: 100));
        services.AddSingleton<IJobStore, JobStore>();
        services.AddHostedService<QueuedHostedService>();
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

        // Day 19: Service Bus, same "zero secrets in config" architecture day-17 used for Azure
        // SQL - no connection string, no key, anywhere, just a fully-qualified namespace (a public
        // DNS name) and a credential.
        //
        // The credential is picked by environment rather than left to DefaultAzureCredential's own
        // fallback chain: confirmed live that in this SDK version, ManagedIdentityCredential's IMDS
        // probe (169.254.169.254) fails with a hard AuthenticationFailedException when there's no
        // metadata endpoint to reach (i.e. anywhere that isn't an actual Azure compute resource),
        // and DefaultAzureCredential does not fall through to AzureCliCredential after that - it
        // just throws. So Development explicitly uses AzureCliCredential (the locally-logged-in
        // `az` session); everywhere else uses DefaultAzureCredential, where an App Service's
        // managed identity actually is reachable via IMDS and this problem doesn't occur.
        var serviceBusOptions = configuration.GetSection(ServiceBusOptions.SectionName).Get<ServiceBusOptions>()
            ?? throw new InvalidOperationException("Missing ServiceBus configuration section.");

        TokenCredential serviceBusCredential = environment.IsDevelopment()
            ? new AzureCliCredential()
            : new DefaultAzureCredential();

        services.AddSingleton(serviceBusOptions);
        services.AddSingleton(_ => new ServiceBusClient(serviceBusOptions.FullyQualifiedNamespace, serviceBusCredential));
        services.AddSingleton<IQuoteEventPublisher, QuoteEventPublisher>();
        services.AddSingleton<IQuoteEventIdStore, QuoteEventIdStore>();
        services.AddSingleton<IEventLogStore<AuditLogEntry>, EventLogStore<AuditLogEntry>>();
        services.AddSingleton<IEventLogStore<NotificationEntry>, EventLogStore<NotificationEntry>>();
        services.AddSingleton<IDeadLetterInspector, DeadLetterInspector>();
        services.AddHostedService<AuditLogProcessorService>();
        services.AddHostedService<NotificationProcessorService>();

        return services;
    }
}