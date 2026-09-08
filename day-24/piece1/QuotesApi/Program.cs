using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Frontend");

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