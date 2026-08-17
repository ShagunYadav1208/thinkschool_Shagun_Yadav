# Day 7 - Piece 2: Window functions

Same `Authors`/`Quotes` schema and seed data as [day-7/piece1](../piece1) (copied in, not
referenced across folders, and piece1's own file was left untouched). This piece asks a different
question of it: not "one row per author" but "one row per quote, with a running count and the
gap since that author's last quote."

## The query

```sql
SELECT
    a.AuthorId,
    a.Name      AS AuthorName,
    q.QuoteText,
    q.CreatedAt,
    ROW_NUMBER() OVER (PARTITION BY a.AuthorId
                        ORDER BY q.CreatedAt)                          AS RunningQuoteCount,
    DATEDIFF(
        DAY,
        LAG(q.CreatedAt) OVER (PARTITION BY a.AuthorId
                                ORDER BY q.CreatedAt),
        q.CreatedAt
    )                                                                  AS DaysSincePreviousQuote
FROM Authors a
JOIN Quotes q ON q.AuthorId = a.AuthorId
ORDER BY a.AuthorId, q.CreatedAt;
```

Full schema + seed data + this query is in [query.sql](query.sql), T-SQL syntax again to match
the rest of the repo's Quotes API stack.

`ROW_NUMBER() OVER (PARTITION BY AuthorId ORDER BY CreatedAt)` restarts at 1 for every author and
climbs by one per quote in date order - that's the running count. `LAG(CreatedAt) OVER (PARTITION
BY AuthorId ORDER BY CreatedAt)` looks one row back *within the same author's partition* and
returns that quote's `CreatedAt`, or `NULL` when there is no previous row (an author's first
quote). Wrapping that in `DATEDIFF(DAY, ..., CreatedAt)` turns "the previous timestamp" into "how
many calendar days since it."

## Sample rows (verified output — all 20 quotes, since that's the full seed set)

Same verification approach as piece1: run for real via Python's bundled `sqlite3` (no `DATEDIFF`
in SQLite, so the gap column is emulated with `julianday(date(...))` differences, matching SQL
Server's `DATEDIFF(DAY, ...)` semantics of counting calendar-day boundaries, not exact 24-hour
multiples).

| AuthorId | AuthorName | QuoteText | CreatedAt | RunningQuoteCount | DaysSincePreviousQuote |
|---|---|---|---|---:|---:|
| 1 | Aria Byte | A clean schema is a promise you keep to your future self. | 2026-01-05T09:00:00 | 1 | *(null)* |
| 1 | Aria Byte | Normalize until it hurts, denormalize until it works. | 2026-03-14T11:30:00 | 2 | 68 |
| 1 | Aria Byte | The index you forgot to add is the query you cannot explain. | 2026-06-02T16:45:00 | 3 | 80 |
| 2 | Milo Query | A join is just a question about how two facts relate. | 2026-01-20T08:15:00 | 1 | *(null)* |
| 2 | Milo Query | Cross joins are honest about the cost everyone else hides. | 2026-02-11T13:00:00 | 2 | 22 |
| 2 | Milo Query | Read the query plan before you blame the database. | 2026-05-19T10:10:00 | 3 | 97 |
| 2 | Milo Query | Every slow report is a join done wrong, dressed up as a business problem. | 2026-07-30T09:40:00 | 4 | 72 |
| 3 | Nadia Index | A CTE names your intent; a subquery hides it. | 2026-02-02T14:20:00 | 1 | *(null)* |
| 3 | Nadia Index | Recursive CTEs are loops that promise to terminate. | 2026-04-08T12:00:00 | 2 | 65 |
| 4 | Theo Cache | Cache invalidation is a conversation, not a switch. | 2026-03-01T17:00:00 | 1 | *(null)* |

Remaining authors (Priya Schema, Owen Table, Lena View, Faye Cursor, Ravi Trigger) follow the same
pattern — full 20-row output is in [query.sql](query.sql)'s companion run; only the first 10 rows
are pasted here per the exercise. Sofia Null has zero quotes, so she has no partition to run a
window over and correctly produces no rows at all in this query (unlike piece1's `LEFT JOIN`
aggregate, which kept her as a zero-count row).

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-7/piece2

## Notes for mentor

Schema and seed data are identical to `day-7/piece1` on purpose — the exercise is about the window
functions, not a new dataset, so I copied the file instead of reusing/importing across folders
(matching the instruction to copy rather than modify or cross-reference existing pieces).

## What did I learn this session?

`LAG()`/`ROW_NUMBER()` with the *same* `PARTITION BY`/`ORDER BY` pair let one query answer two
different-feeling questions ("how many so far" and "how long since last") without a self-join or a
second pass over the table — the partition is what keeps each author's sequence independent of
every other author's, and the ordering is what makes "previous" mean something.

## What would break this?

- Two quotes from the same author sharing an identical `CreatedAt` timestamp: `ROW_NUMBER()` would
  assign them an arbitrary order (whichever the engine visits first), so "running count" and "gap
  since previous" both become non-deterministic for that pair. Adding `QuoteId` as an `ORDER BY`
  tiebreaker would fix it.
- `DATEDIFF(DAY, a, b)` counts calendar-day boundaries crossed, not full 24-hour periods — two
  quotes at `2026-01-05 23:59` and `2026-01-06 00:01` are two minutes apart but show a
  `DaysSincePreviousQuote` of 1, which could read as misleading if someone expects "days" to mean
  "elapsed 24-hour blocks."
- An author's very first quote always shows `NULL` for the gap, which is correct but means any
  downstream consumer doing arithmetic on that column (an average gap, say) needs to explicitly
  filter or `COALESCE` it — silently treating `NULL` as `0` would understate every author's
  average gap.
