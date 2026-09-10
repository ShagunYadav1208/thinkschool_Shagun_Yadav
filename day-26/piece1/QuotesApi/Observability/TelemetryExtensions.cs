using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace QuotesApi.Observability;

/// <summary>
/// Day 26: OpenTelemetry wiring for traces, metrics, and logs. Two exporters, both optional and
/// independently switchable:
///
/// - OTLP, to a local collector (Jaeger in this session - see README "Distributed tracing,
///   verified locally"), added only in Development. This is how this piece's actual trace
///   screenshot was captured - real spans, real trace IDs, just viewed in Jaeger instead of
///   Azure Monitor because there was nowhere else to send them (see AppInsights:ConnectionString
///   below).
/// - Azure Monitor, added whenever "AppInsights:ConnectionString" is non-empty - empty by
///   default (see appsettings.json), same feature-flag-by-absence pattern day-21's Redis
///   connection string and day-25's Auth:Enabled both used. This student's Azure subscription
///   is disabled (spending limit reached - see README), so this exporter is wired and correct
///   but has never actually sent anything anywhere; flipping it on needs nothing but a real
///   connection string once the subscription is usable again.
///
/// The instrumentation itself (which spans get created at all) is identical either way - only
/// where the spans/metrics/logs are SENT depends on these two exporters.
/// </summary>
public static class TelemetryExtensions
{
    public static IServiceCollection AddQuotesApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var appInsightsConnectionString = configuration["AppInsights:ConnectionString"];
        var hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        var otlpEndpoint = new Uri(configuration["Otlp:Endpoint"] ?? "http://localhost:4317");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("QuotesApi", serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    // Spans this app starts deliberately - see QuotesApiActivitySource and
                    // OutboxRelayService's "outbox.relay" span.
                    .AddSource(QuotesApiActivitySource.Name)
                    // Azure SDK client libraries (Azure.Messaging.ServiceBus among them) emit
                    // their own Activities under names starting "Azure." automatically - nothing
                    // exports them unless something is registered to listen, which is what this
                    // wildcard does. This is what turns Service Bus's Send/Process calls into
                    // real spans in the same trace, with zero custom instrumentation code in
                    // QuoteEventPublisher or the consumers - a built-in Azure SDK feature, not
                    // something this app implements.
                    .AddSource("Azure.*")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                if (environment.IsDevelopment())
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnectionString);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (environment.IsDevelopment())
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnectionString);
            });

        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otel =>
            {
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;
                otel.ParseStateValues = true;

                if (environment.IsDevelopment())
                    otel.AddOtlpExporter(otlp => otlp.Endpoint = otlpEndpoint);

                if (hasAppInsights)
                    otel.AddAzureMonitorLogExporter(o => o.ConnectionString = appInsightsConnectionString);
            });
        });

        return services;
    }
}
