using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

// azd's generated Bicep (infra/resources.bicep) sets APPLICATIONINSIGHTS_CONNECTION_STRING as a
// container app env var, but nothing in this app ever read it - the requests/dependencies tables
// stayed empty in App Insights no matter how long telemetry was given to land, confirmed live
// against a real deployment. UseAzureMonitor() auto-detects that same env var and wires ASP.NET
// Core request tracing (plus dependency and exception tracking) into it; it's a no-op locally
// where the variable isn't set.
builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");
app.MapQuoteEndpoints();

app.Run();