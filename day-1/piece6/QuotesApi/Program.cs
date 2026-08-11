using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes") ?? "Data Source=collections.db"));
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapCollectionEndpoints();
app.Run();

public partial class Program;
