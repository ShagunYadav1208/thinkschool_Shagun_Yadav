# Day 10 - Query translation + projections

A runnable console app ([QueryTranslationDemo](QueryTranslationDemo)) - `dotnet run -c Release` seeds
10,000 rows into a real SQLite database (20 of them authored by "Ada Lovelace") and logs the exact SQL
EF Core sends for three query shapes, via `LogTo` filtered to just
`RelationalEventId.CommandExecuted` with `EnableSensitiveDataLogging()` on so parameter values show up
in the log too:

```csharp
var options = new DbContextOptionsBuilder<QuotesDbContext>()
    .UseSqlite(connectionString)
    .LogTo(logSink, new[] { RelationalEventId.CommandExecuted }, LogLevel.Information)
    .EnableSensitiveDataLogging()   // dev-only - never against a database with real user data
    .Options;
```

Full code in [QuotesDbContext.cs](QueryTranslationDemo/QuotesDbContext.cs) and
[Program.cs](QueryTranslationDemo/Program.cs). Every SQL block below is copy-pasted straight from that
real `dotnet run` output - nothing here is a guessed approximation of what EF Core "should" generate.

## 1. Whole-entity query, and its generated SQL

```csharp
var fullEntities = ctx.Quotes.Where(q => q.Author == "Ada Lovelace").ToList();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Ada Lovelace'
```

20 rows back, every column of `Quote` - including `Text` and `CreatedAt`, whether or not anything
downstream actually reads them.

## 2. The projected query + its leaner SQL

```csharp
var projected = ctx.Quotes
    .Where(q => q.Author == "Ada Lovelace")
    .Select(q => new QuoteSummaryDto { Id = q.Id, Author = q.Author, Text = q.Text })
    .ToList();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Ada Lovelace'
```

Same 20 rows, same `WHERE`, but `CreatedAt` is gone from the `SELECT` list entirely - EF Core's LINQ
translator reads the `Select` projection's shape (`QuoteSummaryDto` has `Id`/`Author`/`Text`, no
`CreatedAt`) and only asks SQLite for the columns the DTO actually declares.

## 3. The client-side evaluation I caught

**Buggy:**

```csharp
// BUG: .ToList() runs first - everything after it is LINQ to Objects,
// not a database query anymore.
var everyRow = ctx.Quotes.ToList();
var buggyResult = everyRow.Where(q => q.Author == "Ada Lovelace").ToList();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."Text"
FROM "Quotes" AS "q"
```

Real captured counts: **10,000 rows pulled from the database**, then filtered down to 20 *in memory*,
in C#. The `WHERE` clause never reaches SQL at all - it's not that EF Core translated it badly, it's
that by the time `.Where(...)` runs, the query has already ended (`.ToList()` executed it) and
`.Where()` is now calling `Enumerable.Where` over a plain `List<Quote>`, not
`Queryable.Where` over an `IQueryable<Quote>`. Nothing throws; it just silently fetches the whole
table every time this method runs.

**Fixed:**

```csharp
// FIX: .Where(...) stays part of the query, before .ToList() ends it.
var fixedResult = ctx.Quotes.Where(q => q.Author == "Ada Lovelace").ToList();
```

```sql
SELECT "q"."Id", "q"."Author", "q"."CreatedAt", "q"."Text"
FROM "Quotes" AS "q"
WHERE "q"."Author" = 'Ada Lovelace'
```

Real captured count: **20 rows pulled from the database** - the exact rows needed, nothing else.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-10/piece2

## Notes for mentor

All SQL above and the row counts next to it are real, captured `dotnet run -c Release` output from
`QueryTranslationDemo` - running it again reproduces the same three query shapes and the same
10,000-vs-20 row counts (it re-seeds `translation.db` fresh every run). `EnableSensitiveDataLogging()`
is what makes the log show the literal `'Ada Lovelace'` value instead of a parameter placeholder -
explicitly a dev-only setting, called out as such in `QuotesDbContextFactory`, since it would leak real
user data into logs in production.

## What did I learn this session?

The client-eval bug doesn't look dangerous in the code - `ctx.Quotes.ToList().Where(...)` reads almost
identically to `ctx.Quotes.Where(...).ToList()`, same two method names, same result value, same
20-row answer either way. The only way to actually catch it is to look at what SQL got logged (or the
row count fetched) - by the type system's own rules this compiles fine both ways, because
`List<Quote>.Where()` and `IQueryable<Quote>.Where()` are two different extension methods that happen
to share a name and a shape. Logging the generated SQL in dev isn't a nice-to-have here; it's the only
way this specific mistake becomes visible at all.

## What would break this?

- The projection's win depends entirely on the DTO actually being narrower than the entity. A
  `QuoteSummaryDto` that (accidentally or "just in case") declares every property `Quote` has would
  generate the exact same `SELECT *`-equivalent SQL as the whole-entity query - projecting only helps
  when the projection is honestly smaller.
- This demo's "bug" is deliberately obvious once you know to look (`.ToList()` sitting mid-expression).
  A subtler version - e.g. a method that takes `IEnumerable<Quote>` instead of `IQueryable<Quote>` as a
  parameter, silently forcing materialization at the call boundary - would produce the identical
  full-table-fetch behavior without any `.ToList()` visible at the call site at all.
- `EnableSensitiveDataLogging()` is what let this README quote real parameter values in the SQL above;
  leaving it enabled outside of local development (which this exercise's own DbContext factory
  comment already flags) would mean production logs start containing whatever values users' queries
  actually contain.
