# Day 12 - When to reach for Dapper

The `GetAuthorQuoteFeedQuery` read from [day-12/piece1](../piece1) - the exact read model that
exercise built - reimplemented with Dapper, running side by side with the original EF Core version
against the same 1,000-author / 10,000-quote SQLite database (this project is
`CqrsLiteApi` copied into its own folder, not a modification of piece1). Timing and allocation are
measured for real (`GET /admin/benchmark`, 200 iterations each, warm-up call excluded), not estimated.

## Both implementations

**EF Core** ([GetAuthorQuoteFeedEfQuery.cs](DapperVsEfApi/Read/GetAuthorQuoteFeedEfQuery.cs)):

```csharp
public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedEfQuery request, CancellationToken cancellationToken)
{
    var author = await db.Authors.AsNoTracking()
        .Where(a => a.AuthorId == request.AuthorId)
        .Select(a => new
        {
            a.AuthorId, a.Name,
            TotalQuotes = a.Quotes.Count,
            Quotes = a.Quotes.OrderByDescending(q => q.QuoteId)
                .Select(q => new { q.QuoteId, q.Text, q.CreatedAt })
        })
        .FirstOrDefaultAsync(cancellationToken);

    if (author is null) return null;
    var now = DateTimeOffset.UtcNow;
    var items = author.Quotes
        .Select(q => new QuoteFeedItemDto(q.QuoteId, q.Text, PostedAgoFormatter.Format(now - q.CreatedAt)))
        .ToList();
    return new AuthorQuoteFeedDto(author.AuthorId, author.Name, author.TotalQuotes, items);
}
```

**Dapper** ([GetAuthorQuoteFeedDapperQuery.cs](DapperVsEfApi/Read/GetAuthorQuoteFeedDapperQuery.cs)):

```csharp
private const string Sql = """
    SELECT AuthorId, Name,
           (SELECT COUNT(*) FROM Quotes q WHERE q.AuthorId = a.AuthorId) AS TotalQuotes
    FROM Authors a
    WHERE AuthorId = @AuthorId;

    SELECT QuoteId, Text, CreatedAt AS CreatedAtRaw
    FROM Quotes
    WHERE AuthorId = @AuthorId
    ORDER BY QuoteId DESC;
    """;

public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedDapperQuery request, CancellationToken cancellationToken)
{
    using var connection = connectionFactory.Create();
    using var multi = await connection.QueryMultipleAsync(new CommandDefinition(Sql, new { request.AuthorId }, cancellationToken: cancellationToken));

    var author = await multi.ReadSingleOrDefaultAsync<AuthorRow>();
    if (author is null) return null;

    var quoteRows = (await multi.ReadAsync<QuoteRow>()).ToList();
    var now = DateTimeOffset.UtcNow;
    var items = quoteRows.Select(q =>
    {
        var createdAt = DateTimeOffset.Parse(q.CreatedAtRaw, CultureInfo.InvariantCulture);
        return new QuoteFeedItemDto(q.QuoteId, q.Text, PostedAgoFormatter.Format(now - createdAt));
    }).ToList();
    return new AuthorQuoteFeedDto(author.AuthorId, author.Name, author.TotalQuotes, items);
}
```

Both return byte-for-byte identical JSON for the same author (verified: `GET
/authors/500/feed-ef` and `GET /authors/500/feed-dapper` produce the same response).

## SQL comparison

EF's generated SQL (captured via `LogTo`, one round trip, correlated `COUNT` subquery + `LEFT JOIN`):

```sql
SELECT "a0"."AuthorId", "a0"."Name", "a0"."c", "q0"."QuoteId", "q0"."Text", "q0"."CreatedAt"
FROM (
    SELECT "a"."AuthorId", "a"."Name", (
        SELECT COUNT(*) FROM "Quotes" AS "q" WHERE "a"."AuthorId" = "q"."AuthorId") AS "c"
    FROM "Authors" AS "a"
    WHERE "a"."AuthorId" = @request_AuthorId
    LIMIT 1
) AS "a0"
LEFT JOIN "Quotes" AS "q0" ON "a0"."AuthorId" = "q0"."AuthorId"
ORDER BY "a0"."AuthorId", "q0"."QuoteId" DESC
```

Dapper's SQL (hand-written, two statements batched into one round trip via `QueryMultiple`):

```sql
SELECT AuthorId, Name, (SELECT COUNT(*) FROM Quotes q WHERE q.AuthorId = a.AuthorId) AS TotalQuotes
FROM Authors a WHERE AuthorId = @AuthorId;

SELECT QuoteId, Text, CreatedAt AS CreatedAtRaw
FROM Quotes WHERE AuthorId = @AuthorId ORDER BY QuoteId DESC;
```

Same information either way, one round trip either way - the difference isn't the SQL's shape here,
it's everything EF Core does *around* running that SQL: LINQ-to-SQL translation, query-plan caching,
and materialization through its change-tracking-aware pipeline (even with `AsNoTracking()`, entities
still pass through more machinery than Dapper's direct reflection-emit mapper).

## Timing and allocation comparison

Real captured output from `GET /admin/benchmark?authorId=500&iterations=200` (fresh warm-up call per
implementation excluded from the measured loop, `GC.Collect()` forced before each measured call so
allocation is attributable to that call alone):

| | Mean | Median | Min | Max | Mean allocated |
|---|---:|---:|---:|---:|---:|
| EF Core | 2.59 ms | 1.59 ms | 1.02 ms | 35.86 ms | 31,646 bytes |
| Dapper | 0.78 ms | 0.54 ms | 0.35 ms | 5.85 ms | 8,455 bytes |

Repeated against a different author (`authorId=250`) for confidence: EF median 1.13ms vs. Dapper
median 0.44ms - same ~2.5-3x gap both times, and the **allocation ratio was the more stable number**
across both runs (~3.7x-3.8x less allocated per call with Dapper, both times), consistent with this
session's earlier finding (Day 10) that allocation counts are steadier than wall-clock timing under
light, unloaded conditions.

## Two real bugs hit and fixed while writing the Dapper version

Both are left in the code's own comments rather than hidden, since they're exactly the kind of thing
the "one paragraph rule" below is about:

1. **`int` vs `long`**: SQLite returns `INTEGER` columns as `Int64`. Mapping to a positional `record`
   (`AuthorRow(int AuthorId, ...)`) failed outright - Dapper's constructor-matching needs an *exact*
   parameter-type match. Switched to plain classes with settable `int` properties, which Dapper
   coerces automatically via its property-setter path.
2. **`DateTimeOffset` round-trip**: EF Core's SQLite provider has an internal value converter that
   silently turns the `TEXT` column back into a `DateTimeOffset` on the way out. Raw ADO.NET/Dapper
   has no such converter - the column arrives as the plain ISO-8601 string EF Core wrote, and Dapper's
   default deserializer can't cast a `string` to `DateTimeOffset` via `Convert.ChangeType`. Fixed by
   reading it as a `string` and parsing it explicitly.

## The one-paragraph rule for Dapper vs. EF

Default to EF Core for anything that writes data, anything whose shape will keep changing as the
domain evolves, and any read that isn't actually hot - LINQ, change tracking, and migrations pay for
themselves in maintainability almost everywhere. Reach for Dapper only for a *read* path you've
already measured to be both frequently-hit and dominated by per-call overhead rather than by the
underlying query's own cost (this exercise's ~3x/~3.7x gap came from EF's pipeline overhead on a query
that only ever returns ~10 rows, not from the SQL being slow) - and go in expecting to hand-manage the
things EF Core was quietly doing for you, like type coercion and value conversion between the database
and your CLR types, because Dapper won't catch those mismatches until they throw at runtime.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-12/piece2

## Notes for mentor

`DapperVsEfApi` is `CqrsLiteApi` (day-12/piece1) copied into this folder and extended - piece1's
project itself was never touched. Every number and SQL block above is real, captured output: the
`/admin/benchmark` endpoint runs both handlers directly (through the same `IMediator.Send` pipeline
each production request would use, just without the HTTP layer around it) so the comparison measures
the actual code paths this app runs, not a synthetic microbenchmark harness. Both real bugs hit while
building the Dapper handler are documented above and in the code's own comments rather than silently
fixed and forgotten.

## What did I learn this session?

The two bugs were more instructive than the timing numbers. EF Core's SQLite provider was quietly
doing type coercion and value conversion (`long`->`int`, `string`<->`DateTimeOffset`) that I'd stopped
noticing was even happening, because I'd never had to write it myself. Dropping to Dapper meant
picking that work back up by hand - which is exactly the trade a teammate needs to understand before
reaching for it, not just the speedup.

## What would break this?

- This benchmark measures one specific query shape (single author, ~10 quotes) on SQLite with no
  network hop to the database. A query returning thousands of rows, or a real network round trip to a
  separate database server, could shift where the actual bottleneck sits - EF's per-call overhead
  might become a rounding error next to genuine I/O or serialization cost at that point.
- The Dapper version's manual `DateTimeOffset` parsing works because this schema only ever wrote it in
  the one ISO-8601 format EF Core happens to use. A raw SQL query against a column populated by some
  other tool, or a database migrated from a different format, could hand Dapper a string
  `DateTimeOffset.Parse` can't read - EF Core's value converter would have handled it consistently
  either way, at the cost of not being visible in the code at all.
- The two hand-written SQL statements in the Dapper version aren't validated against the schema at
  compile time the way the EF LINQ query is - a column rename in the `Quotes` table would break the
  Dapper query only at runtime, on whatever request happens to hit it first, while EF Core's provider
  would have failed the same way but with a stack trace that at least starts from LINQ, not raw SQL
  text.
