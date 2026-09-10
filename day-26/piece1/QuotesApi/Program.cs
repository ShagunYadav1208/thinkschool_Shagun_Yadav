using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddQuotesApiTelemetry(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Frontend");

// Mirrors AddInfrastructure's IsAuthEnabled check - only wire the auth
// middleware in when "Auth:Enabled" is true. Calling these unconditionally
// while disabled would be harmless today (no scheme is registered to
// authenticate against), but keeping both checks explicit and in sync is
// less surprising than relying on that.
if (InfrastructureExtensions.IsAuthEnabled(builder.Configuration))
{
    app.UseAuthentication();
    app.UseAuthorization();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

    // The Sqlite migrations checked into Migrations/ are provider-specific and don't
    // apply to Azure SQL. Rather than maintaining a second, parallel migrations
    // history for a three-column table, the Azure SQL path uses EnsureCreated - a
    // deliberate simplification for this exercise's schema, called out in the README
    // under "what would break this" (a real schema change would need a proper
    // SqlServer migrations history instead).
    if (db.Database.IsSqlServer())
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
}

app.MapQuoteEndpoints();
app.MapJobEndpoints();
app.MapServiceBusEndpoints();
app.MapOutboxEndpoints();
app.MapCacheEndpoints();
app.MapResilienceEndpoints();

app.Run();