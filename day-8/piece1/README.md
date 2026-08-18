# Day 8 - Clustered vs non-clustered indexes

No local SQL Server instance was available on this machine (same situation `day-3/piece6` and
`day-7/piece1` ran into), but this exercise is specifically about `SET STATISTICS IO` and execution
plans, and those numbers mean nothing if they're guessed rather than measured. So instead of
substituting SQLite, a real SQL Server 2022 container (`mcr.microsoft.com/mssql/server:2022-latest`
- the same image `day-3/piece6`'s Testcontainers suite pulls) was started via Docker, and
`query.sql` was run against it directly with `sqlcmd`. Every number below is a genuine capture, not
an estimate.

`Orders` (100,000 rows, generated set-based - `CustomerId` cycles across 2,000 values so each
customer has ~50 orders, `CreatedAt` is deterministic so the range query always matches the same
10,080 rows) starts life as a heap: no `PRIMARY KEY`, no indexes at all. A clustered index and two
non-clustered indexes get added one at a time, and the same three queries get re-run after each
addition with `SET STATISTICS IO ON`.

## Index DDL

```sql
CREATE TABLE Orders (
    OrderId     INT             NOT NULL,
    CustomerId  INT             NOT NULL,
    Status      NVARCHAR(20)    NOT NULL,
    OrderTotal  DECIMAL(10,2)   NOT NULL,
    CreatedAt   DATETIME2       NOT NULL
);
-- 100,000 rows loaded here as a heap (no PK yet) -- see query.sql

ALTER TABLE Orders ADD CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId);

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId);

CREATE NONCLUSTERED INDEX IX_Orders_CreatedAt ON Orders(CreatedAt) INCLUDE (CustomerId);
```

Full schema, the 100k-row generator, and every `SET STATISTICS IO` checkpoint (run in order, so the
script is reproducible top to bottom) are in [query.sql](query.sql).

## Logical reads before/after each index

| Query | Predicate | Reads before | Index added | Reads after | Plan operator, before -> after |
|-------|-----------|-------------:|-------------|------------:|---------------------------------|
| Q1 | `OrderId = 54321` (point lookup) | 670 | `PK_Orders` CLUSTERED (`OrderId`) | **3** | Table Scan -> Clustered Index Seek |
| Q2 | `CustomerId = 777` (equality, 50 rows) | 676\* | `IX_Orders_CustomerId` (`CustomerId`) | **164** | Clustered Index Scan -> Index Seek + Key Lookup (Nested Loops) |
| Q3 | `CreatedAt` range (10,080 rows) | 676\* | `IX_Orders_CreatedAt` (`CreatedAt`) INCLUDE (`CustomerId`) | **30** | Clustered Index Scan -> Index Seek (covering, no lookup) |

\* Q2 and Q3's "before" is measured right after the clustered index went on (Step 1), since that's
the state they were actually in when their own index was added in Step 2/3. The true from-scratch
heap baseline (zero indexes at all) was identical for all three queries: **670** logical reads via a
plain Table Scan - a heap scan reads every page regardless of what the predicate is, which is exactly
why Q2 and Q3 barely moved (670 -> 676) when the clustered index went on: neither predicate touches
`OrderId`, so the engine still visits every row, just now via a Clustered Index Scan instead of a
Table Scan.

Plan operators above came from `SET STATISTICS PROFILE ON`'s `PhysicalOp` column (no SSMS was
available to pull a graphical plan) - it reports the identical operator names SSMS's graphical plan
renders as boxes: `Table Scan`, `Clustered Index Seek`, `Index Seek`, `Clustered Index Seek ...
LOOKUP`, `Nested Loops`.

## Why each index changed the plan

**Q1** - `OrderId` is the clustering key, so once it exists the engine finds row 54321 by walking the
B-tree (root page, intermediate page, leaf page - 3 reads) instead of scanning all 670 heap pages
looking for a match.

**Q2** - `CustomerId` isn't the clustering key, so `IX_Orders_CustomerId` only narrows down *which*
rows match (an Index Seek, ~50 of them). `Status` and `OrderTotal` aren't columns in that index, so
each of the 50 matching rows needs a separate Key Lookup back into the clustered index - a Nested
Loops join between the non-clustered seek and the lookup. That's why 164 reads is a big win over 676
but nowhere near Q1's 3: every matching row still costs a lookup.

**Q3** - `IX_Orders_CreatedAt(CreatedAt) INCLUDE (CustomerId)` is a *covering* index for this exact
query: `OrderId` (the clustering key) rides along on every non-clustered index row for free, and
`CustomerId` is explicitly `INCLUDE`d, so both selected columns are already sitting in the index leaf
- there's nothing left to look up. A single Index Seek over the 10,080 matching rows is enough.

## Write-side cost

Inserting the same 5,000-row batch cost **5,034** logical reads against the plain heap, but **48,639**
(38,495 on the table itself + 10,144 on a `Worktable` index-maintenance spool) once the clustered
index and both non-clustered indexes existed - about **9.7x** more page touches for the identical
insert, because every row now has to be placed into three separate B-trees instead of just appended
to a heap.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-8/piece1

## Notes for mentor

No local SQL Server or SSMS was available on this machine, so a real SQL Server 2022 container was
started with Docker (`docker run mcr.microsoft.com/mssql/server:2022-latest`) and `query.sql` was
piped into it with `sqlcmd` - the same tool, same image, as `day-3/piece6`'s Testcontainers-backed
integration suite, just driven directly instead of through .NET. Every logical-read number above is
copy-pasted from that session's real `SET STATISTICS IO` output (`query.sql`'s inline comments quote
the same numbers next to the statement that produced them), and the plan operators came from
`SET STATISTICS PROFILE ON` since there was no SSMS to open a graphical plan in - that setting reports
the actual (not estimated) plan as text, with the same operator names SSMS shows as boxes.

## What did I learn this session?

The clustered index only helps the query that filters on its own key. Adding `PK_Orders CLUSTERED
(OrderId)` fixed Q1 (670 -> 3 reads) but left Q2 and Q3 essentially untouched (670 -> 676, if
anything slightly worse) because neither of their predicates is on `OrderId` - they still have to
visit every row, just via a Clustered Index Scan instead of a Table Scan. Whether a non-clustered
index actually removes the Key Lookup afterward depends entirely on whether its leaf row already
carries every column the query selects (Q3's `INCLUDE`) or not (Q2's index, which doesn't cover
`Status`/`OrderTotal`).

## What would break this?

- Widening either `SELECT` list without widening the matching `INCLUDE` to match would silently
  reintroduce Key Lookups and blow the read count back up - Q3's index only covers the exact column
  set (`OrderId`, `CustomerId`) it was built for; adding `OrderTotal` to that `SELECT` would send it
  back to a Nested Loops + Lookup plan like Q2's.
- The write-cost wall-clock numbers (CPU/elapsed time) actually moved *around* between runs of an
  otherwise-identical insert - an earlier isolated test even had the indexed insert clock in faster
  than the heap insert, the opposite of what the logical-reads count predicts. A single 5,000-row
  batch on an idle, single-user container isn't enough to pin down timing reliably; the logical-reads
  count is deterministic and reproducible run to run (verified by re-running the whole script twice),
  wall-clock timing at this scale isn't.
- `CustomerId`'s even distribution (2,000 values spread uniformly across 100k rows, ~50 rows each)
  is what makes `IX_Orders_CustomerId` reliably worth using. A real `CustomerId` column with a
  handful of whale accounts holding a large fraction of all rows would push the optimizer to prefer
  scanning over seeking-plus-looking-up for those specific values - the same reason `Status` (only 4
  distinct values here) isn't indexed at all in this exercise: an index that low-selectivity is rarely
  worth its write cost.
