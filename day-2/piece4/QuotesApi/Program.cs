using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuoteInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapQuoteEndpoints();
app.Run();

public partial class Program;
