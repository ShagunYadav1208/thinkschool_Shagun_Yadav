# Day 11 - Drop p99 by 10x

The fix for [day-11/piece1](../piece1)'s N+1-plus-missing-index endpoint, built as its own project
([FixedEndpointApi](FixedEndpointApi)) copied from piece1's `SlowEndpointApi` and then changed - not
a rewrite of piece1 itself. Same real-infrastructure approach as every other piece this week: SQL
Server 2022 in Docker, `k6` for load, `LogTo`/`STATISTICS PROFILE` for the real SQL and plans. Every
number below is copy-pasted from an actual run.

## The two changes

**1. Fixed the model.** `Quote.AuthorId` in piece1 was a bare `int` with no EF Core relationship at
all - the real reason no index existed. This piece adds the missing piece of the model:

```csharp
// Quote.cs
public int AuthorId { get; set; }
public Author Author { get; set; } = null!;

// AppDbContext.cs
modelBuilder.Entity<Quote>()
    .HasOne(q => q.Author)
    .WithMany(a => a.Quotes)
    .HasForeignKey(q => q.AuthorId);
```

Once `AuthorId` is a real foreign key, EF Core's "index every FK" convention takes over -
`IX_Quotes_AuthorId` gets created automatically by `EnsureCreated()`, with no separate migration or
manual DDL needed (confirmed: `SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('Quotes')`
shows it right after startup).

**2. Fixed the query**, using the exercise's named technique - `Include` with a split query, not a
loop:

```csharp
// BEFORE (/authors-summary-before) - unchanged from piece1's anti-pattern
var authors = await db.Authors.AsNoTracking().ToListAsync();
foreach (var author in authors)
{
    var quoteCount = await db.Quotes.AsNoTracking().CountAsync(q => q.AuthorId == author.AuthorId);
    // ...
}

// AFTER (/authors-summary-after)
var authors = await db.Authors.AsNoTracking()
    .Include(a => a.Quotes)
    .AsSplitQuery()
    .ToListAsync();
var result = authors.Select(a => new { a.AuthorId, a.Name, quoteCount = a.Quotes.Count });
```

## Before/after p99 (10 concurrent VUs, 30s, `k6`, same load shape as piece1)

| | p50 | p99 | SQL round trips/request |
|---|---:|---:|---:|
| Before (N+1, no index) | 4.87s - 5.68s | **5.43s - 6.5s** | 1,001 |
| After (`Include`+`AsSplitQuery`, indexed) | 153ms - 177ms | **244ms - 295ms** | 2 |

(Range = two repeated runs each, to show this isn't a cherry-picked single sample.)

**p99 improvement: ~18x-27x depending which pair of runs you compare** - comfortably past the
exercise's 10x target, using the most conservative (slowest-after, fastest-before) combination.

## Before/after SQL

Full text in [offending-sql-and-fix.txt](offending-sql-and-fix.txt). Before: 1 query for authors,
then this exact statement 1,000 times:

```sql
SELECT COUNT(*) FROM [Quotes] AS [q] WHERE [q].[AuthorId] = @author_AuthorId
```

After: exactly 2 queries, full stop - **the count doesn't scale with the number of authors**:

```sql
SELECT [a].[AuthorId], [a].[Name] FROM [Authors] AS [a] ORDER BY [a].[AuthorId]

SELECT [q].[QuoteId], [q].[AuthorId], [q].[CreatedAt], [q].[QuoteText], [a].[AuthorId]
FROM [Authors] AS [a]
INNER JOIN [Quotes] AS [q] ON [a].[AuthorId] = [q].[AuthorId]
```

## Before/after execution plans

Full text in [execution-plans-before-after.txt](execution-plans-before-after.txt).

**Before** - `Clustered Index Scan`, 117 logical reads, **times 1,000**:

```
|--Clustered Index Scan(OBJECT:([Quotes].[PK_Quotes]), WHERE:([AuthorId]=[@author_AuthorId]))
```

**After** - two scans, **135 logical reads total for the entire request**:

```
Statement 1: |--Clustered Index Scan(OBJECT:([Authors].[PK_Authors]), ORDERED FORWARD)      -- 9 reads
Statement 2: |--Hash Match(Inner Join, HASH:([a].[AuthorId])=([q].[AuthorId]))
                  |--Clustered Index Scan(OBJECT:([Authors].[PK_Authors]))                   -- 9 reads
                  |--Clustered Index Scan(OBJECT:([Quotes].[PK_Quotes]))                      -- 117 reads
```

One honest surprise: statement 2's optimizer doesn't use `IX_Quotes_AuthorId` at all - it needs every
row of `Quotes` regardless of `AuthorId` (all 10,000, for all 1,000 authors, in one shot), so a full
clustered-index scan genuinely is the cheapest plan; the narrower index has nothing to offer a query
that already needs 100% of the table. A repeat load test with the index dropped confirmed p99 gets
meaningfully worse anyway (~494ms vs. ~244-295ms with it) even though the single-connection plan
above doesn't reference it - likely a buffer-pool/paging effect under concurrent load that a lone
`STATISTICS PROFILE` capture doesn't show, flagged honestly rather than explained away with false
confidence.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-11/piece2

## Notes for mentor

`FixedEndpointApi` is `SlowEndpointApi` copied into this folder and then changed (model fixed to a
real FK relationship, query fixed to `Include`+`AsSplitQuery`) - piece1's project itself was never
touched. `/authors-summary-before` is intentionally identical in shape to piece1's slow endpoint, so
this piece's own baseline is self-contained rather than a number borrowed from elsewhere; it was
re-verified to emit the same 1,001 queries and the same `Clustered Index Scan` plan before any
"after" numbers were trusted. Every SQL block and execution plan above is real `LogTo`/
`STATISTICS PROFILE` output; every p50/p99 pair is a real `k6 run` result, repeated twice per
condition specifically so the 10x claim wasn't resting on a single lucky sample.

## What did I learn this session?

The index turned out to be almost beside the point for *this* particular fixed query - the query
needs every row anyway, so there's no smaller subset for an index to help find. The ~20-25x win came
almost entirely from turning 1,001 round trips into 2, which is a good reminder that "add the missing
index" and "eliminate the N+1" are not really two halves of one fix - they address two different
costs (per-query execution cost vs. per-request round-trip count), and a query shape can make one of
them irrelevant while the other still dominates.

## What would break this?

- `Include(a => a.Quotes)` pulls back full `Quote` entities - `QuoteText` and `CreatedAt` included -
  just to compute a count. That's the trade-off against piece1's leaner projection-only fix (a
  correlated-subquery `SELECT COUNT(*)`, no `Quote` rows materialized at all): this fix eliminates the
  N+1 completely, but at the cost of transferring and allocating 10,000 full rows client-side instead
  of 1,000 integers. For a summary endpoint that only needs a count, the projection approach from
  piece1 would likely still out-perform this one - worth measuring before assuming `Include` is
  always the fix, not just an available one.
- `AsSplitQuery()` issues its queries as separate round trips deliberately (to avoid the cartesian
  explosion a single JOIN query would cause here), which means they're no longer guaranteed
  transactionally consistent with each other by default - a row inserted between statement 1 and
  statement 2 could show up in one result but not the other. Fine for a read-only summary endpoint;
  not something to reach for blindly on a query where that inconsistency window matters.
- This fix depends on `Quotes.AuthorId` being a real, EF-Core-visible foreign key. Piece1's whole bug
  existed *because* that wasn't true; anyone "fixing" a similar endpoint without also fixing the model
  underneath it would still get the query-shape improvement from `Include`, but would need to add the
  index by hand (as piece1 in fact did) since the automatic-FK-index convention would never fire.
