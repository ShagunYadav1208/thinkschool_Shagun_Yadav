using System.Diagnostics;

namespace QuotesApi.Observability;

/// <summary>
/// One custom ActivitySource for spans this app starts deliberately (as opposed to the ones
/// ASP.NET Core, EF Core, and the Azure SDKs create automatically via their own instrumentation).
/// Must be registered by name in the OpenTelemetry TracerProviderBuilder (see
/// TelemetryExtensions.AddQuotesApiTelemetry) - an ActivitySource nobody's listening to just
/// creates Activity objects that are silently thrown away, not exported anywhere.
/// </summary>
public static class QuotesApiActivitySource
{
    public const string Name = "QuotesApi";

    public static readonly ActivitySource Instance = new(Name, "1.0.0");
}
