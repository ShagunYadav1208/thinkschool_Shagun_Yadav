using Microsoft.EntityFrameworkCore;
using SlowEndpointApi.Data;
using SlowEndpointApi.Models;

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
    db.Database.EnsureCreated();

    var authors = Enumerable.Range(1, AuthorCount)
        .Select(i => new Author { Name = $"Author {i}" })
        .ToList();
    db.Authors.AddRange(authors);
    db.SaveChanges(); // assigns each Author its identity-generated AuthorId

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
// The deliberately slow endpoint: N+1 over authors -> quotes.
// One query for all authors, then one MORE query per author to count
// that author's quotes - 1,001 round trips for 1,000 authors, each of
// which is a full table scan of Quotes because Quotes.AuthorId has no
// index (see Quote.cs / AppDbContext.cs for why).
// ============================================================
app.MapGet("/authors-summary-slow", async (AppDbContext db) =>
{
    var authors = await db.Authors.AsNoTracking().ToListAsync();
    var result = new List<object>(authors.Count);
    foreach (var author in authors)
    {
        // N+1: a separate round trip per author, inside the loop.
        var quoteCount = await db.Quotes.AsNoTracking().CountAsync(q => q.AuthorId == author.AuthorId);
        result.Add(new { author.AuthorId, author.Name, quoteCount });
    }
    return Results.Ok(result);
});

// ============================================================
// The fixed endpoint: one query, a GROUP BY join, run only after the
// index exists. Used for comparison, not part of the baseline profiling.
// ============================================================
app.MapGet("/authors-summary-fast", async (AppDbContext db) =>
{
    var result = await db.Authors.AsNoTracking()
        .Select(a => new
        {
            a.AuthorId,
            a.Name,
            quoteCount = db.Quotes.Count(q => q.AuthorId == a.AuthorId)
        })
        .ToListAsync();
    return Results.Ok(result);
});

// ============================================================
// Admin endpoints used only to move between profiling phases -
// add/remove the "missing" index against the already-running database,
// the same way a DBA would apply it without redeploying the app.
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
