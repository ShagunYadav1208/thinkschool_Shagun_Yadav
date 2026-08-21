# Day 11 - Profile a slow endpoint

A real, running ASP.NET Core API ([SlowEndpointApi](SlowEndpointApi)) against real SQL Server 2022
(Docker, since no local instance was available - same setup as `day-8`/`day-9`), profiled under real
concurrent load with `k6` (installed via `winget install k6` - `bombardier` wasn't available and k6
installed cleanly). Every number below is copy-pasted from an actual `k6 run` or `sqlcmd` session, not
estimated.

## The deliberately slow endpoint

`GET /authors-summary-slow` returns each of 1,000 authors alongside their quote count, out of 10,000
total quotes (10 per author). It's built with two problems on purpose:

```csharp
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
```

```csharp
// Quote.cs - AuthorId is a plain int, with NO EF Core relationship configured
// (no navigation property, no HasOne/WithMany). EF Core auto-indexes every
// foreign key IT KNOWS ABOUT - a column that merely looks like one, with no
// relationship metadata, gets no index for free. That's the realistic way
// this actually happens in real codebases.
public int AuthorId { get; set; }
```

Full code in [SlowEndpointApi](SlowEndpointApi/Program.cs); the k6 script is [load-test.js](load-test.js).

## Baseline p50/p99 (10 concurrent VUs, 30 seconds, `k6`)

```
http_req_duration..............: avg=4.86s min=4.17s med=4.94s max=5.3s
    p(50)=4.94s p(90)=5.2s p(95)=5.22s p(99)=5.27s
http_reqs......................: 70     2.048849/s
```

**p50 = 4.94s, p99 = 5.27s**, and the whole 30-second run only completed 70 requests across 10
concurrent users - roughly 2 requests/second, total.

## The offending SQL

One HTTP GET to `/authors-summary-slow` emits **1,001 separate SQL commands** - captured via EF
Core's `LogTo` at `Information` level (full text in [offending-sql.txt](offending-sql.txt)):

```sql
SELECT [a].[AuthorId], [a].[Name]
FROM [Authors] AS [a]
```

...followed by this exact statement, once per author, 1,000 times, only the parameter value changing:

```sql
SELECT COUNT(*)
FROM [Quotes] AS [q]
WHERE [q].[AuthorId] = @author_AuthorId
```

## The plan

One of those 1,000 identical per-author queries, captured with `SET STATISTICS PROFILE ON` directly
against SQL Server (full output in [execution-plans.txt](execution-plans.txt)):

```
SELECT COUNT(*) FROM [Quotes] AS [q] WHERE [q].[AuthorId] = @author_AuthorId

|--Compute Scalar(...)
     |--Stream Aggregate(DEFINE:([Expr1002]=Count(*)))
          |--Clustered Index Scan(OBJECT:([SlowEndpointApi].[dbo].[Quotes].[PK_Quotes] AS [q]),
                 WHERE:([q].[AuthorId]=[@author_AuthorId]))

Table 'Quotes'. Scan count 1, logical reads 117
```

**Clustered Index Scan** - with no index on `AuthorId`, SQL Server reads all 10,000 rows (117 logical
reads) to find the ~10 that match, every single time. 1,000 of these per request is ~117,000 logical
reads to answer one HTTP GET.

## The two biggest problems

1. **N+1 query pattern** - one query to fetch authors, then one more query *per author* inside a
   loop, instead of a single query that returns everyone's count at once. This is what turns "1
   database round trip" into "1,001," and round-trip latency (not raw query cost) is what dominates
   under load.
2. **Missing index on `Quotes.AuthorId`** - because that column carries no EF Core relationship
   metadata, EF Core's "index every foreign key" convention never sees it as one, so no index gets
   created. Every one of those 1,000 per-author queries pays for a full table scan that a single
   non-clustered index would turn into a seek.

## Isolating each problem's contribution

| Endpoint | Index? | p50 | p99 | Round trips/request |
|---|---|---:|---:|---:|
| `/authors-summary-slow` | No | **4.94s** | **5.27s** | 1,001 |
| `/authors-summary-slow` | Yes | 2.83s | 4.45s | 1,001 |
| `/authors-summary-fast` (single query) | Yes | **10.77ms** | **25.72ms** | 1 |

Adding the index alone (still N+1) roughly halves p50 - real, but nowhere close to enough, because
1,000 round trips are still 1,000 round trips. Fixing the N+1 pattern into one query (with the index
in place) is what actually matters: **p50 drops from 4.94s to 10.77ms (~460x), p99 from 5.27s to
25.72ms (~205x)**, and the same 30-second, 10-VU load test went from **70 completed requests to
25,479**. The fixed query's plan (`execution-plans.txt`) does one `Index Scan` + one `Stream
Aggregate` + one `Merge Join` - **29 total logical reads for the entire request**, versus ~117,000 for
the N+1-over-no-index version.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-11/piece1

## Notes for mentor

No local SQL Server or load-testing tool was available on this machine - SQL Server 2022 ran in
Docker (same pattern as `day-8`/`day-9`), and `k6` was installed fresh via `winget install k6`
(`bombardier` wasn't already present, and k6 was the one that installed cleanly). Every number in this
README - the k6 summary blocks, the SQL, and the `STATISTICS PROFILE` output - is real, captured
output from actually running this API and hitting it, not narrated. `/admin/create-index` and
`/admin/drop-index` exist purely so the same running app could move between "before" and "after"
profiling phases without a redeploy, exactly like a DBA adding a missing index to a live system.

## What did I learn this session?

The index alone (2.83s p50) looked like a real win until it sat next to the single-query fix
(10.77ms). It's tempting to treat "add the missing index" as *the* fix for a slow endpoint, but here
it only shaved the cost of each of 1,000 round trips - it did nothing about there being 1,000 round
trips in the first place. Network/round-trip latency multiplied by N is a completely different kind of
cost than per-query execution time, and no amount of indexing fixes the first one.

## What would break this?

- This N+1 uses `CountAsync` in a loop, which is the most obviously wrong shape. A subtler version -
  lazy-loading navigation properties enabled, where `author.Quotes.Count` inside the loop looks like
  a harmless property access - produces the identical 1,001-round-trip problem with no `await` or
  loop-body query visible at the call site at all.
- The baseline numbers here (p50 ≈ 4.94s at just 10 concurrent users) are already this bad on an
  otherwise-idle machine with nothing else contending for the SQL Server container's connections. A
  real production database under other concurrent traffic, or a connection pool limit lower than the
  number of concurrent N+1 requests in flight, would make this same code fail with connection-pool
  timeouts instead of just running slowly.
- The fixed endpoint's dramatic win depends on the index actually matching the query's filter column.
  If a future change filtered on a *different* column (say, a date range) without a matching index,
  the single-query version would still be one round trip - correctly avoiding the N+1 - but could
  still degrade to a full scan per request under the exact same "missing index" problem this exercise
  demonstrates, just without the multiplying effect of N+1 on top of it.
