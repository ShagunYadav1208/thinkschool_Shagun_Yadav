using System.Diagnostics;
using System.Globalization;
using ChangeTrackerBenchmark;
using Microsoft.EntityFrameworkCore;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const int RowCount = 10_000;
const int Iterations = 5;

var dbPath = Path.Combine(AppContext.BaseDirectory, "benchmark.db");
if (File.Exists(dbPath)) File.Delete(dbPath);
var connectionString = $"Data Source={dbPath}";

DbContextOptions<QuotesDbContext> BuildOptions() =>
    new DbContextOptionsBuilder<QuotesDbContext>()
        .UseSqlite(connectionString)
        .Options;

var options = BuildOptions();

// ============================================================
// Setup: create schema, seed 10,000 rows.
// ============================================================
using (var setupContext = new QuotesDbContext(options))
{
    setupContext.Database.EnsureCreated();

    var quotes = Enumerable.Range(1, RowCount).Select(i => new Quote
    {
        Author = $"Author {i % 500}",
        Text = $"Quote number {i} - the change tracker is invisible until it bites you.",
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
    });
    setupContext.Quotes.AddRange(quotes);
    setupContext.SaveChanges();
}
Console.WriteLine($"Seeded {RowCount:N0} rows into {dbPath}");
Console.WriteLine();

// ============================================================
// Warm-up: pay for EF Core's model build + query compilation + SQLite
// connection open ONCE, outside the measured runs, so the timed runs
// below measure the read itself, not first-run JIT/model-build cost.
// ============================================================
using (var warmupContext = new QuotesDbContext(options))
{
    _ = warmupContext.Quotes.Take(1).ToList();
}
using (var warmupContext = new QuotesDbContext(options))
{
    _ = warmupContext.Quotes.AsNoTracking().Take(1).ToList();
}

// ============================================================
// Demo 1: identity resolution.
// A tracked context returns the SAME instance for the same primary key
// across two separate queries in that context. AsNoTracking does not -
// it has no identity map, so every query materializes a fresh instance.
// ============================================================
Console.WriteLine("=== Identity resolution ===");
using (var identityContext = new QuotesDbContext(options))
{
    var first = identityContext.Quotes.First(q => q.Id == 1);
    var second = identityContext.Quotes.First(q => q.Id == 1);
    Console.WriteLine($"Tracked:      ReferenceEquals(first read, second read) = {ReferenceEquals(first, second)}");
}
using (var noTrackIdentityContext = new QuotesDbContext(options))
{
    var first = noTrackIdentityContext.Quotes.AsNoTracking().First(q => q.Id == 1);
    var second = noTrackIdentityContext.Quotes.AsNoTracking().First(q => q.Id == 1);
    Console.WriteLine($"AsNoTracking: ReferenceEquals(first read, second read) = {ReferenceEquals(first, second)}");
}
Console.WriteLine();

// ============================================================
// Demo 2: is the entity actually tracked?
// ============================================================
Console.WriteLine("=== ChangeTracker.Entries() after a full 10k-row read ===");
using (var trackedDemoContext = new QuotesDbContext(options))
{
    trackedDemoContext.Quotes.ToList();
    Console.WriteLine($"After tracked read:      ChangeTracker.Entries().Count() = {trackedDemoContext.ChangeTracker.Entries().Count():N0}");
}
using (var noTrackDemoContext = new QuotesDbContext(options))
{
    noTrackDemoContext.Quotes.AsNoTracking().ToList();
    Console.WriteLine($"After AsNoTracking read: ChangeTracker.Entries().Count() = {noTrackDemoContext.ChangeTracker.Entries().Count():N0}");
}
Console.WriteLine();

// ============================================================
// Demo 3: the read-path win. Fresh DbContext per iteration (so the
// tracked run's identity map/change tracker doesn't carry over between
// iterations), same 10k-row table, same query shape, only the tracking
// behavior differs.
// ============================================================
static (double MeanMs, double MeanAllocatedBytes) Measure(Func<QuotesDbContext> newContext, Func<QuotesDbContext, List<Quote>> read, int iterations)
{
    var times = new double[iterations];
    var allocs = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        using var context = newContext();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var result = read(context);
        sw.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();
        times[i] = sw.Elapsed.TotalMilliseconds;
        allocs[i] = after - before;
        GC.KeepAlive(result);
    }
    return (times.Average(), allocs.Average());
}

var tracked = Measure(() => new QuotesDbContext(options), ctx => ctx.Quotes.ToList(), Iterations);
var noTracking = Measure(() => new QuotesDbContext(options), ctx => ctx.Quotes.AsNoTracking().ToList(), Iterations);

Console.WriteLine($"=== 10,000-row read, averaged over {Iterations} iterations (fresh DbContext each time) ===");
Console.WriteLine($"{"",-14}{"Mean time (ms)",-18}{"Mean allocated (bytes)",-24}{"Mean allocated (MB)"}");
Console.WriteLine($"{"Tracked",-14}{tracked.MeanMs,-18:F2}{tracked.MeanAllocatedBytes,-24:N0}{tracked.MeanAllocatedBytes / 1024 / 1024:F2}");
Console.WriteLine($"{"AsNoTracking",-14}{noTracking.MeanMs,-18:F2}{noTracking.MeanAllocatedBytes,-24:N0}{noTracking.MeanAllocatedBytes / 1024 / 1024:F2}");
Console.WriteLine();
Console.WriteLine($"Time ratio (tracked / no-tracking):      {tracked.MeanMs / noTracking.MeanMs:F2}x");
Console.WriteLine($"Allocation ratio (tracked / no-tracking): {tracked.MeanAllocatedBytes / noTracking.MeanAllocatedBytes:F2}x");
