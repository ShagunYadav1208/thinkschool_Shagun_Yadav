using CqrsLiteApi.Data;
using CqrsLiteApi.Domain;
using CqrsLiteApi.Read;
using CqrsLiteApi.Write;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Path.Combine(AppContext.BaseDirectory, "cqrslite.db");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (File.Exists(dbPath)) File.Delete(dbPath);
    db.Database.EnsureCreated();

    db.Authors.AddRange(
        new Author { Name = "Ada Lovelace" },
        new Author { Name = "Grace Hopper" });
    db.SaveChanges();
}

// ============================================================
// WRITE: the command path. Validates, checks the domain invariant
// (author must exist), persists a normalized Quote row.
// ============================================================
app.MapPost("/quotes", async (CreateQuoteCommand command, IMediator mediator) =>
{
    try
    {
        var quoteId = await mediator.Send(command);
        return Results.Created($"/quotes/{quoteId}", new { quoteId });
    }
    catch (ValidationException ex)
    {
        return Results.ValidationProblem(ex.Errors.ToDictionary(
            e => e.PropertyName,
            e => new[] { e.ErrorMessage }));
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
});

// ============================================================
// READ: the query path. No validation, no domain invariants to check -
// just shape the data the "author quote feed" screen wants.
// ============================================================
app.MapGet("/authors/{authorId:int}/feed", async (int authorId, IMediator mediator) =>
{
    var feed = await mediator.Send(new GetAuthorQuoteFeedQuery(authorId));
    return feed is not null ? Results.Ok(feed) : Results.NotFound();
});

app.Run();
