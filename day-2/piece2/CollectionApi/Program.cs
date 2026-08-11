using CollectionApi.Data;
using CollectionApi.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCollectionInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CollectionsDbContext>();

    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
}

app.MapCollectionEndpoints();

app.Run();

public partial class Program;
