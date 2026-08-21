using Microsoft.EntityFrameworkCore;
using FixedEndpointApi.Data;
using FixedEndpointApi.Models;

const int AuthorCount = 1_000;
const int QuotesPerAuthor = 10;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("AppDb")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureDeleted();
    db.Database.EnsureCreated(); // creates IX_Quotes_AuthorId too - see AppDbContext

    var authors = Enumerable.Range(1, AuthorCount)
        .Select(i => new Author { Name = $"Author {i}" })
        .ToList();
    db.Authors.AddRange(authors);
    db.SaveChanges();

    var quotes = new List<Quote>(AuthorCount * QuotesPerAuthor);
    foreach (var author in authors)
    {
        for (var j = 0; j < QuotesPerAuthor; j++)
        {
            quotes.Add(new Quote
            {
                AuthorId = author.AuthorId,
                QuoteText = $"Quote {j} from author {author.AuthorId}.",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-(author.AuthorId * QuotesPerAuthor + j))
            });
        }
    }
    db.Quotes.AddRange(quotes);
    db.SaveChanges();
}

app.MapGet("/health", () => Results.Ok("healthy"));

// ============================================================
// BEFORE: the same anti-pattern as day-11/piece1's /authors-summary-slow -
// one query for authors, then one more query PER AUTHOR inside a loop.
// Kept here, unmodified in shape, so the "before" measurement in this piece
// is a fair, self-contained baseline rather than a number quoted from
// somewhere else.
// ============================================================
app.MapGet("/authors-summary-before", async (AppDbContext db) =>
{
    var authors = await db.Authors.AsNoTracking().ToListAsync();
    var result = new List<object>(authors.Count);
    foreach (var author in authors)
    {
        var quoteCount = await db.Quotes.AsNoTracking().CountAsync(q => q.AuthorId == author.AuthorId);
        result.Add(new { author.AuthorId, author.Name, quoteCount });
    }
    return Results.Ok(result);
});

// ============================================================
// AFTER: Include(...).AsSplitQuery() instead of a loop. EF Core issues
// exactly TWO queries total - one for Authors, one for ALL Quotes ordered
// by AuthorId - and stitches each author's quotes into a.Quotes in memory.
// No N+1: the query count no longer scales with the number of authors.
// ============================================================
app.MapGet("/authors-summary-after", async (AppDbContext db) =>
{
    var authors = await db.Authors.AsNoTracking()
        .Include(a => a.Quotes)
        .AsSplitQuery()
        .ToListAsync();
    var result = authors.Select(a => new { a.AuthorId, a.Name, quoteCount = a.Quotes.Count });
    return Results.Ok(result);
});

// ============================================================
// Admin endpoints - move the already-running app between "before" (no
// index) and "after" (indexed) phases without a redeploy, same as piece1.
// ============================================================
app.MapPost("/admin/create-index", async (AppDbContext db) =>
{
    await db.Database.ExecuteSqlRawAsync(
        "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Quotes_AuthorId') " +
        "CREATE NONCLUSTERED INDEX IX_Quotes_AuthorId ON Quotes(AuthorId);");
    return Results.Ok("index created");
});

app.MapPost("/admin/drop-index", async (AppDbContext db) =>
{
    await db.Database.ExecuteSqlRawAsync(
        "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Quotes_AuthorId') " +
        "DROP INDEX IX_Quotes_AuthorId ON Quotes;");
    return Results.Ok("index dropped");
});

app.Run();
