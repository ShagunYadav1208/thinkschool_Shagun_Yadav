using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using QuotesApi.BackgroundJobs;
using QuotesApi.Caching;
using QuotesApi.Data;
using QuotesApi.Outbox;
using QuotesApi.Repositories;
using QuotesApi.Resilience;
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

        // Day 20: the outbox relay. Reads QuotesDbContext (scoped) via its own scope per poll tick,
        // and publishes through the same IQuoteEventPublisher singleton day-19 already registered -
        // it's the only thing that calls it now (see QuoteEndpointExtensions).
        services.AddHostedService<OutboxRelayService>();

        // Day 21: HybridCache in front of the hot single-quote read (GET /api/quotes/{id}).
        // HybridCache always keeps an in-process L1 (the "in-memory" half); it picks up an L2
        // automatically the moment an IDistributedCache is registered in the container - no
        // explicit wiring beyond registering both. That's what AddStackExchangeRedisCache below
        // does. Its built-in "stampede protection" (the docs' term is single-flight/request
        // coalescing) means concurrent GetOrCreateAsync calls for the *same key* while it's
        // missing share one in-flight factory execution instead of each starting their own -
        // see QuoteEndpointExtensions for where that factory is the actual DB read.
        services.Configure<CacheDemoOptions>(configuration.GetSection(CacheDemoOptions.SectionName));
        services.AddSingleton<ICacheMetrics, CacheMetrics>();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                // Expiration governs the (optional) L2/Redis entry; LocalCacheExpiration governs
                // the in-process L1 copy. Kept equal here - nothing about this exercise needs the
                // two tiers to expire on different schedules, but HybridCache lets them.
                Expiration = TimeSpan.FromSeconds(30),
                LocalCacheExpiration = TimeSpan.FromSeconds(30),
            };
        });

        // L2: Redis. Connection string only, no credential - this session's scope is a local
        // Docker Redis (see README's "Redis backing" note: Azure Cache for Redis is retired on
        // this subscription in favor of Azure Managed Redis, and provisioning a live instance
        // for a single load-test session wasn't worth the cost here). Swapping in a real Azure
        // Managed Redis endpoint later is a one-line change to this connection string plus the
        // same "AzureCliCredential locally / DefaultAzureCredential in Azure" TokenCredential
        // split already used for Service Bus above - HybridCache's own code doesn't change at
        // all, since it only ever talks to IDistributedCache, not to Redis directly.
        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "quotes:";
            });
        }
        else
        {
            // Confirmed live (Day 21 deployment): HybridCache.RemoveAsync throws when NO
            // IDistributedCache at all is registered - a real edge case local testing never hit
            // because Redis was always up there. AddDistributedMemoryCache is HybridCache's own
            // documented placeholder for "no real L2" - functionally a no-op second tier, but it
            // keeps an IDistributedCache present so RemoveAsync (and anything else that assumes
            // an L2 exists) doesn't crash. Not a workaround for a config mistake; this is the
            // correct thing to register whenever Redis genuinely isn't available.
            services.AddDistributedMemoryCache();
        }

        // Day 22: Polly, wrapping the "quote enrichment" outbound dependency (see
        // FlakyDependencyHandler - a deterministic, controllable stand-in for a real third-party
        // API, so the circuit breaker's open/half-open/close cycle can be reproduced on demand
        // rather than waiting on some real service's actual uptime).
        services.AddSingleton<FlakyDependencyState>();
        services.AddSingleton<IResilienceMetrics, ResilienceMetrics>();
        services.AddTransient<FlakyDependencyHandler>();

        services
            .AddHttpClient<IQuoteEnrichmentClient, QuoteEnrichmentClient>(client =>
            {
                // Never actually resolved - FlakyDependencyHandler is the primary handler, so no
                // DNS lookup or socket ever happens. Kept HTTP-shaped anyway (a real
                // HttpRequestMessage/HttpResponseMessage flows through the whole pipeline) so the
                // resilience configuration below is identical to what a real outbound dependency
                // would use.
                client.BaseAddress = new Uri("http://flaky-dependency.internal/");
            })
            .ConfigurePrimaryHttpMessageHandler<FlakyDependencyHandler>()
            .AddResilienceHandler("quote-enrichment-pipeline", (builder, context) =>
            {
                var metrics = context.ServiceProvider.GetRequiredService<IResilienceMetrics>();
                var logger = context.ServiceProvider.GetRequiredService<ILogger<IQuoteEnrichmentClient>>();
                var stateProvider = new CircuitBreakerStateProvider();
                metrics.AttachStateProvider(stateProvider);

                HttpMethod[] idempotentMethods = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Options];

                builder
                    // BULKHEAD (Polly v8's replacement for v7's BulkheadPolicy is a rate limiter
                    // strategy - functionally the same "cap concurrent usage of this dependency"
                    // idea). Added first, so it's the OUTERMOST layer: it gates entry to the whole
                    // pipeline, meaning one logical call plus all of its own retries only ever
                    // occupies one bulkhead slot, not one per retry attempt.
                    .AddConcurrencyLimiter(permitLimit: 5, queueLimit: 5)
                    // RETRY: only for idempotent methods (this client only ever issues GETs, but
                    // the check is explicit rather than assumed) and never for a circuit already
                    // open - retrying into an open breaker just delays the fail-fast it exists to
                    // provide.
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        ShouldHandle = args =>
                        {
                            if (args.Outcome.Exception is BrokenCircuitException or IsolatedCircuitException)
                                return ValueTask.FromResult(false);

                            var method = args.Outcome.Result?.RequestMessage?.Method;
                            var isIdempotent = method is null || idempotentMethods.Contains(method);
                            var isFailure = args.Outcome.Exception is not null
                                || (args.Outcome.Result is not null && !args.Outcome.Result.IsSuccessStatusCode);

                            return ValueTask.FromResult(isIdempotent && isFailure);
                        },
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        Delay = TimeSpan.FromMilliseconds(200),
                        UseJitter = true,
                        OnRetry = args =>
                        {
                            metrics.RecordRetry();
                            logger.LogWarning(
                                "Retrying quote enrichment (attempt {AttemptNumber}) after {Reason}",
                                args.AttemptNumber + 1,
                                args.Outcome.Exception?.GetType().Name ?? args.Outcome.Result?.StatusCode.ToString());
                            return default;
                        },
                    })
                    // CIRCUIT BREAKER: trips once a rolling 10s window sees at least 8 attempts
                    // (initial calls + their retries all count) with a >=50% failure ratio - short-
                    // circuits with BrokenCircuitException for BreakDuration instead of continuing
                    // to hammer a dependency that's already down.
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        FailureRatio = 0.5,
                        MinimumThroughput = 8,
                        SamplingDuration = TimeSpan.FromSeconds(10),
                        BreakDuration = TimeSpan.FromSeconds(8),
                        StateProvider = stateProvider,
                        OnOpened = args =>
                        {
                            metrics.RecordTransition(CircuitState.Open);
                            logger.LogWarning("Circuit breaker OPENED for {BreakDuration}", args.BreakDuration);
                            return default;
                        },
                        OnClosed = args =>
                        {
                            metrics.RecordTransition(CircuitState.Closed);
                            logger.LogInformation("Circuit breaker CLOSED - dependency recovered");
                            return default;
                        },
                        OnHalfOpened = args =>
                        {
                            metrics.RecordTransition(CircuitState.HalfOpen);
                            logger.LogInformation("Circuit breaker HALF-OPEN - trial call in flight");
                            return default;
                        },
                    })
                    // TIMEOUT: bounds a single attempt (not the whole retry sequence) so a hung
                    // dependency can't tie up a request indefinitely.
                    .AddTimeout(new HttpTimeoutStrategyOptions
                    {
                        Timeout = TimeSpan.FromSeconds(1),
                        OnTimeout = args =>
                        {
                            metrics.RecordTimeout();
                            logger.LogWarning("Attempt timed out after {Timeout}", args.Timeout);
                            return default;
                        },
                    });
            });

        return services;
    }
}