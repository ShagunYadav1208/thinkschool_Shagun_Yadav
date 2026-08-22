using System.Diagnostics;
using DapperVsEfApi.Data;
using DapperVsEfApi.Domain;
using DapperVsEfApi.Read;
using DapperVsEfApi.Write;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

const int AuthorCount = 1_000;
const int QuotesPerAuthor = 10;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Path.Combine(AppContext.BaseDirectory, "dappervsef.db");
var connectionString = $"Data Source={dbPath}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton(new SqliteConnectionFactory(connectionString));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (File.Exists(dbPath)) File.Delete(dbPath);
    db.Database.EnsureCreated();

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
                Text = $"Quote {j} from author {author.AuthorId}.",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-(author.AuthorId * QuotesPerAuthor + j))
            });
        }
    }
    db.Quotes.AddRange(quotes);
    db.SaveChanges();
}

// ============================================================
// WRITE: unchanged from day-12/piece1 - this exercise is about the read
// path, the write path is only here because the feature needs one.
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
// READ: the same author-quote-feed question, answered two ways.
// ============================================================
app.MapGet("/authors/{authorId:int}/feed-ef", async (int authorId, IMediator mediator) =>
{
    var feed = await mediator.Send(new GetAuthorQuoteFeedEfQuery(authorId));
    return feed is not null ? Results.Ok(feed) : Results.NotFound();
});

app.MapGet("/authors/{authorId:int}/feed-dapper", async (int authorId, IMediator mediator) =>
{
    var feed = await mediator.Send(new GetAuthorQuoteFeedDapperQuery(authorId));
    return feed is not null ? Results.Ok(feed) : Results.NotFound();
});

// ============================================================
// BENCHMARK: run both handlers directly (bypassing HTTP, so only the
// data-access path itself is measured) N times each against the same
// author, after a warm-up call each pays for JIT/model-build/connection
// setup outside the measured loop.
// ============================================================
app.MapGet("/admin/benchmark", async (int authorId, int iterations, IMediator mediator) =>
{
    await mediator.Send(new GetAuthorQuoteFeedEfQuery(authorId));
    await mediator.Send(new GetAuthorQuoteFeedDapperQuery(authorId));

    var ef = await Measure(() => mediator.Send(new GetAuthorQuoteFeedEfQuery(authorId)), iterations);
    var dapper = await Measure(() => mediator.Send(new GetAuthorQuoteFeedDapperQuery(authorId)), iterations);

    return Results.Ok(new { authorId, iterations, ef, dapper });
});

app.Run();

static async Task<BenchmarkResult> Measure<T>(Func<Task<T>> action, int iterations)
{
    var timesMs = new double[iterations];
    var allocs = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();
        timesMs[i] = sw.Elapsed.TotalMilliseconds;
        allocs[i] = after - before;
    }
    Array.Sort(timesMs);
    return new BenchmarkResult(
        MeanMs: timesMs.Average(),
        MedianMs: timesMs[iterations / 2],
        MinMs: timesMs[0],
        MaxMs: timesMs[^1],
        MeanAllocatedBytes: allocs.Average());
}

record BenchmarkResult(double MeanMs, double MedianMs, double MinMs, double MaxMs, double MeanAllocatedBytes);
