using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QueryTranslationDemo;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const int RowCount = 10_000;
const string TargetAuthor = "Ada Lovelace";

var dbPath = Path.Combine(AppContext.BaseDirectory, "translation.db");
if (File.Exists(dbPath)) File.Delete(dbPath);
var connectionString = $"Data Source={dbPath}";

// ============================================================
// Setup: seed 10,000 rows. Every 500th row is authored by "Ada Lovelace"
// (20 rows total) so a WHERE-filtered query returns a small, meaningful
// subset instead of "everything" or "one row".
// ============================================================
using (var setupContext = QuotesDbContextFactory.Create(connectionString, _ => { }))
{
    setupContext.Database.EnsureCreated();
    var quotes = Enumerable.Range(1, RowCount).Select(i => new Quote
    {
        Author = i % 500 == 0 ? TargetAuthor : $"Author {i % 499}",
        Text = $"Quote number {i} - projections only fetch what the DTO actually needs.",
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
    });
    setupContext.Quotes.AddRange(quotes);
    setupContext.SaveChanges();
}
Console.WriteLine($"Seeded {RowCount:N0} rows ({RowCount / 500} authored by \"{TargetAuthor}\") into {dbPath}");
Console.WriteLine();

static List<string> RunLogged(string connectionString, Action<QuotesDbContext> action)
{
    var log = new List<string>();
    using var context = QuotesDbContextFactory.Create(connectionString, s => log.Add(s));
    action(context);
    return log;
}

// ============================================================
// Demo 1: whole-entity query - every column of Quote comes back,
// including Text and CreatedAt, even though nothing downstream uses them.
// ============================================================
Console.WriteLine("=== 1. Whole-entity query ===");
List<Quote>? fullEntities = null;
var fullEntityLog = RunLogged(connectionString, ctx =>
{
    fullEntities = ctx.Quotes.Where(q => q.Author == TargetAuthor).ToList();
});
Console.WriteLine($"Rows returned: {fullEntities!.Count}");
Console.WriteLine("Generated SQL:");
Console.WriteLine(string.Join(Environment.NewLine, fullEntityLog));
Console.WriteLine();

// ============================================================
// Demo 2: same filter, rewritten as a projection into QuoteSummaryDto
// (Id, Author, Text - no CreatedAt). The generated SQL should only
// SELECT the three columns the DTO actually declares.
// ============================================================
Console.WriteLine("=== 2. Projected query (.Select(x => new QuoteSummaryDto {...})) ===");
List<QuoteSummaryDto>? projected = null;
var projectedLog = RunLogged(connectionString, ctx =>
{
    projected = ctx.Quotes
        .Where(q => q.Author == TargetAuthor)
        .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author, Text = q.Text })
        .ToList();
});
Console.WriteLine($"Rows returned: {projected!.Count}");
Console.WriteLine("Generated SQL:");
Console.WriteLine(string.Join(Environment.NewLine, projectedLog));
Console.WriteLine();

// ============================================================
// Demo 3: the accidental client-side evaluation this exercise asks to
// catch. BUGGY: .ToList() materializes the WHOLE table first, then
// .Where(...) filters in memory (LINQ to Objects) - the WHERE never
// reaches SQL at all. FIXED: push .Where(...) before .ToList() so it
// compiles into the SQL and only the matching rows ever leave the DB.
// ============================================================
Console.WriteLine("=== 3. Accidental client-side evaluation - BUGGY ===");
int rowsPulledFromDb = 0;
List<Quote>? buggyResult = null;
var buggyLog = RunLogged(connectionString, ctx =>
{
    // BUG: ToList() runs first - this is LINQ to Objects from here on.
    var everyRow = ctx.Quotes.ToList();
    rowsPulledFromDb = everyRow.Count;
    buggyResult = everyRow.Where(q => q.Author == TargetAuthor).ToList();
});
Console.WriteLine($"Rows pulled from the database: {rowsPulledFromDb}");
Console.WriteLine($"Rows after in-memory filter:    {buggyResult!.Count}");
Console.WriteLine("Generated SQL (note: no WHERE clause at all):");
Console.WriteLine(string.Join(Environment.NewLine, buggyLog));
Console.WriteLine();

Console.WriteLine("=== 3. Same query, FIXED ===");
List<Quote>? fixedResult = null;
var fixedLog = RunLogged(connectionString, ctx =>
{
    // FIX: .Where(...) stays inside the query, before ToList() ends it.
    fixedResult = ctx.Quotes.Where(q => q.Author == TargetAuthor).ToList();
});
Console.WriteLine($"Rows pulled from the database: {fixedResult!.Count}");
Console.WriteLine("Generated SQL (WHERE pushed down to SQLite):");
Console.WriteLine(string.Join(Environment.NewLine, fixedLog));
