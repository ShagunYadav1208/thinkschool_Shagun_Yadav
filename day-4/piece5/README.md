# Day 4 - Connect to Azure Application Insights

This piece builds on Day 4 Piece 4 (Serilog + OpenTelemetry with a local Jaeger exporter) and adds
the production side: traces, logs, and metrics also export to Azure Application Insights, with the
connection string sourced from Key Vault at runtime — never hardcoded, never in a file checked into
source control.

## What's real here, and what isn't

I don't have an Azure subscription or credentials available in this environment (`az account show`
fails with "Please run az login"), so I could not actually create an App Insights resource, a Key
Vault, or a live alert to screenshot. What I could do, and did:

- Write and **build** the actual application code against the real NuGet packages (verified: the
  app restores, compiles, runs, and its existing 13 integration tests still pass with nothing
  configured — Key Vault and Azure Monitor are no-ops until their config keys exist).
- Write and **compile** (`az bicep build`, zero errors/warnings) the Infrastructure-as-Code for the
  App Insights resource, the action group, and the alert — this is the same file a real deployment
  would `az deployment group create` against; I just couldn't run that last step here.
- Write the KQL queries against the real, standard Application Insights schema (`requests`,
  `traces`, `dependencies`), which don't need a live resource to be correct.

Everything below is real, working code and IaC — the one manual step left for you is `az login` on
your own subscription and running the deployment (see "To actually deploy" below).

## App Insights connection setup

**Packages** (`QuotesIntegrationApi.csproj`) — note the exercise names
`Microsoft.Azure.Monitor.OpenTelemetry.AspNetCore`; that package was renamed, so this uses the
current, real, shipping ID:

```xml
<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.6.0" />
<PackageReference Include="Azure.Identity" Version="1.21.0" />
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.5.1" />
```

**Key Vault, before anything else reads configuration** (`Program.cs`):

```csharp
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}
```

`KeyVault:Uri` is set as an Azure App Service application setting (never in `appsettings.json`).
`DefaultAzureCredential` resolves to the App Service's system-assigned managed identity when
deployed — no client secret exists anywhere, which is the actual point of using Entra ID here
rather than a Key Vault access key. Locally, with `KeyVault:Uri` unset, this block is a no-op and
the app behaves exactly like Piece 4.

**Azure Monitor exporter, added on top of (not instead of) local OTLP export** (`Program.cs`):

```csharp
var tracing = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options => { /* local Jaeger, from Piece 4 */ }));

var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    tracing.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}
```

`ApplicationInsights:ConnectionString` comes from Key Vault (secret name
`ApplicationInsights--ConnectionString` — `--` is Key Vault's hierarchical-key separator, mapping
to config key `ApplicationInsights:ConnectionString`). `UseAzureMonitor()` doesn't just add a trace
exporter — it also captures `ILogger` output and standard ASP.NET Core/EF Core/HttpClient metrics,
so the same `logger.LogInformation("Created quote {QuoteId} for user {UserId}", ...)` calls from
Piece 3 become queryable `traces` rows in App Insights with `QuoteId`/`UserId` as
`customDimensions`, with no additional code.

## Infrastructure as code (`infra/main.bicep`)

Provisions:
- A workspace-based Application Insights resource (the only kind Azure creates today) + its
  required Log Analytics workspace.
- An action group with one email receiver.
- A **log alert** (`Microsoft.Insights/scheduledQueryRules`) — not a metric alert, deliberately:
  "average response time of `POST /api/quotes`" is a per-endpoint average, which isn't a
  resource-level metric Azure Monitor exposes by name. It's a KQL query evaluated on a schedule:

  ```kql
  requests
  | where name == "POST /api/quotes"
  | summarize AvgDurationMs = avg(duration) by bin(timestamp, 5m)
  ```

  with `threshold: 500`, `timeAggregation: Average`, evaluated every 5 minutes — exactly "average
  response time of `POST /api/quotes` exceeds 500ms over 5 minutes → email," and nothing else, so
  it only pages when this one thing needs attention.

### To actually deploy

```bash
az login
az group create --name quotes-integration-rg --location eastus
az deployment group create \
  --resource-group quotes-integration-rg \
  --template-file day-4/piece5/infra/main.bicep \
  --parameters alertEmail=you@example.com
```

Then, on the App Service hosting the app: enable a system-assigned managed identity, grant it
`Key Vault Secrets User` on the Key Vault, store the App Insights connection string (from the
deployment's `appInsightsConnectionString` output) as a Key Vault secret named
`ApplicationInsights--ConnectionString`, and set the app setting `KeyVault:Uri` to the vault's URI.

## KQL queries (`infra/queries.kql`)

**Exercise deliverable — slowest 10 requests in the last hour:**

```kql
requests
| where timestamp > ago(1h)
| order by duration desc
| take 10
| project timestamp, name, duration, resultCode, success, cloud_RoleName, operation_Id
```

The file also has the exercise's own example query adapted to this app's actual log fields, the
exact query behind the alert, and a trace-correlation query joining `requests` + `dependencies` by
`operation_Id` (the same trace ID Serilog and Jaeger both use, from Piece 4).

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece5

## Notes for mentor

No Azure subscription available in this environment, so the resources themselves aren't live —
everything above is real code/IaC I built and verified as far as I could without one (compiles,
runs, tests pass, Bicep compiles cleanly). The one thing I'd want a second pair of eyes on before a
real deploy: the log alert's `evaluationFrequency` and `windowSize` are both `PT5M` here, which
means a slow 5-minute window could take up to ~10 minutes to first page (schedule interval plus
window) — worth deciding if that lag is acceptable for this endpoint before treating it as a
paging-grade alert.

## What did I learn this session?

`UseAzureMonitor()` is not "one more exporter to add to the list" — it replaces the mental model
entirely. Traces, logs, *and* metrics all funnel through it in one call, including `ILogger` output
that has nothing to do with `System.Diagnostics.Activity`. That's why the KQL example query in the
exercise (`customDimensions.UserId`) works against a `traces` row that came from a plain
`logger.LogInformation` call — the structured property became a custom dimension automatically,
without me writing an enricher or a converter for it.

## What would break this?

`DefaultAzureCredential` tries several credential sources in order (managed identity, environment
variables, Azure CLI, Visual Studio, etc.) and the first one that doesn't outright fail wins — on a
misconfigured App Service (managed identity not enabled, or enabled but missing the Key Vault RBAC
role), it won't throw a clear "identity not found" error early; it'll fail deep inside the first
Key Vault call with a generic auth exception, which is a confusing failure mode for whoever's
on-call at 2am. Worth adding a fail-fast startup check that Key Vault is actually reachable before
`app.Run()`, rather than discovering it on the first request that needs a secret.
