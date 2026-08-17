# Day 7 - Joins and CTEs at depth

The Week-1 Quotes DB (`day-1/piece3`, `day-3/piece6`) stores `Author` as a flat `NVARCHAR` column
directly on `Quotes` - there's no separate `Authors` table, so there's nothing to join. This piece
uses a fresh two-table schema instead: `Authors` and `Quotes`, with `Quotes.AuthorId` as a foreign
key, so the exercise actually exercises a join rather than just an aggregate over one table. Ten
authors are seeded, one of them (`Sofia Null`) deliberately given zero quotes, to prove the query
keeps quote-less authors in the result rather than silently dropping them.

## The query

```sql
WITH AuthorQuotesRanked AS (
    SELECT
        a.AuthorId,
        a.Name                                                            AS AuthorName,
        q.QuoteText,
        q.CreatedAt,
        COUNT(q.QuoteId)   OVER (PARTITION BY a.AuthorId)                 AS QuoteCount,
        ROW_NUMBER()       OVER (PARTITION BY a.AuthorId
                                  ORDER BY q.CreatedAt DESC)               AS RecencyRank
    FROM Authors a
    LEFT JOIN Quotes q ON q.AuthorId = a.AuthorId
)
SELECT
    AuthorId,
    AuthorName,
    QuoteCount,
    QuoteText  AS MostRecentQuote,
    CreatedAt  AS MostRecentQuoteAt
FROM AuthorQuotesRanked
WHERE RecencyRank = 1
ORDER BY QuoteCount DESC, AuthorName;
```

Full schema + seed data + this query is in [query.sql](query.sql), written against SQL Server /
Azure SQL syntax to match the stack the rest of this repo's Quotes API uses.

The CTE does two jobs in one pass over the `LEFT JOIN`: `COUNT(q.QuoteId) OVER (PARTITION BY
a.AuthorId)` gives every row for an author the same total quote count (counting only non-null
`QuoteId`s, so an author with zero quotes correctly counts as 0, not 1), and `ROW_NUMBER() OVER
(PARTITION BY a.AuthorId ORDER BY q.CreatedAt DESC)` ranks that author's quotes newest-first. The
outer `SELECT` just keeps `RecencyRank = 1` - the newest quote row per author, already carrying
that author's count alongside it.

## Result set (all 10 authors)

Verified by actually running the schema, seed data, and query - via Python's bundled `sqlite3`
module, since no local SQL Server instance was available at hand (same situation as `day-3/piece6`'s
design-time migration tooling). SQLite 3.50 supports the same window functions and CTE syntax used
here, so the result shape is representative; only vendor-specific types (`DATETIME2`, `IDENTITY`)
differ, not the query logic.

| AuthorId | AuthorName   | QuoteCount | MostRecentQuote                                                            | MostRecentQuoteAt   |
|----------|--------------|-----------:|-----------------------------------------------------------------------------|----------------------|
| 2        | Milo Query   | 4          | Every slow report is a join done wrong, dressed up as a business problem.  | 2026-07-30T09:40:00 |
| 1        | Aria Byte    | 3          | The index you forgot to add is the query you cannot explain.              | 2026-06-02T16:45:00 |
| 5        | Priya Schema | 3          | Constraints are documentation the database actually enforces.             | 2026-06-30T10:00:00 |
| 8        | Faye Cursor  | 2          | Set-based thinking is the difference between a query and a program.       | 2026-04-14T16:30:00 |
| 3        | Nadia Index  | 2          | Recursive CTEs are loops that promise to terminate.                       | 2026-04-08T12:00:00 |
| 6        | Owen Table   | 2          | Wide tables age like unattended gardens.                                  | 2026-05-05T11:20:00 |
| 9        | Ravi Trigger | 2          | Side effects belong in the application, not in the INSERT statement.      | 2026-06-16T14:05:00 |
| 7        | Lena View    | 1          | A view is a query wearing a name tag.                                     | 2026-03-27T09:10:00 |
| 4        | Theo Cache   | 1          | Cache invalidation is a conversation, not a switch.                       | 2026-03-01T17:00:00 |
| 10       | Sofia Null   | 0          | *(null)*                                                                   | *(null)*            |

Only 10 authors exist in the seed data, so this table is the full result, not a truncated top 10.

## Why a CTE here over a correlated subquery in the SELECT

A correlated subquery version would need two separate subqueries per author in the `SELECT` list -
one `(SELECT COUNT(*) FROM Quotes WHERE AuthorId = a.AuthorId)` and one `(SELECT TOP 1 QuoteText
FROM Quotes WHERE AuthorId = a.AuthorId ORDER BY CreatedAt DESC)` - which means the engine
re-scans (or re-seeks) `Quotes` for every author, twice, once per row of the outer query. The CTE
scans `Quotes` once via the `LEFT JOIN`, computes both the count and the recency rank in the same
windowed pass, and the outer `SELECT` just filters `RecencyRank = 1` - one join, one pass, both
answers.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-7/piece1

## Notes for mentor

`Authors`/`Quotes` here is a new schema, not the Week-1 Quotes DB, because that DB's `Quotes.Author`
is a plain string column with no `Authors` table behind it - there was no foreign key to join
against. Author names and quote text are synthetic placeholders made up for this exercise, not real
attributed quotes.

## What did I learn this session?

`COUNT(x) OVER (PARTITION BY ...)` counts only non-null `x`, which is exactly what makes the
`LEFT JOIN` + window-function combination work for authors with zero matching rows: the single
null-padded row from the outer join contributes 0 to the count instead of 1. Without that, every
quote-less author would show `QuoteCount = 1` instead of `0`.

## What would break this?

- Two quotes from the same author with the *exact* same `CreatedAt` timestamp: `ROW_NUMBER()`
  breaks the tie arbitrarily (whichever row the engine visits first), so "most recent" would be
  non-deterministic. Adding `QuoteId DESC` as a tiebreaker in the `ORDER BY` inside `ROW_NUMBER()`
  would fix that.
- An `Authors` row with no `Quotes` rows relies on the `LEFT JOIN` direction being exactly right -
  swap it to an `INNER JOIN` (or reverse it to `Quotes LEFT JOIN Authors`) and `Sofia Null` silently
  disappears from the result instead of showing `QuoteCount = 0`.
- This assumes one `Authors` row per distinct author name. If the same author name were entered
  twice as two separate `Authors` rows (a data-entry duplicate, not a schema constraint), their
  quotes would be split across two counts instead of merged into one.
