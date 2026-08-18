# Day 8 - Covering indexes + INCLUDEd columns

Same "no local SQL Server, so use a real one" approach as [day-8/piece1](../piece1): a SQL Server
2022 container (`mcr.microsoft.com/mssql/server:2022-latest`) was started via Docker, `query.sql` was
run against it with `sqlcmd`, and every number below is a genuine capture - the before plan really
does show a Key Lookup, and the after plan really doesn't.

`Orders` (100,000 rows, `CustomerId` cycling across 2,000 values so `CustomerId = 777` always matches
exactly 50 rows) starts with a clustered PK on `OrderId` and one non-clustered index on `CustomerId`
that does **not** cover the exercise query - `Status` and `OrderTotal` aren't in it, so the engine has
to look each of those 50 rows up separately in the clustered index. That index then gets rebuilt with
`Status`/`OrderTotal` as `INCLUDE`d columns, and the same query is re-run.

## Before: the query doing a key lookup

```sql
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId);

SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
```

Actual plan (`SET STATISTICS PROFILE ON` - no SSMS was available to pull a graphical plan, but this
reports the same operators SSMS would draw as boxes):

```
SELECT [OrderId],[Status],[OrderTotal] FROM [Orders] WHERE [CustomerId]=@1
  |--Nested Loops(Inner Join, OUTER REFERENCES:(...) WITH UNORDERED PREFETCH)
       |--Index Seek(OBJECT:(IX_Orders_CustomerId), SEEK:(CustomerId=(777)) ORDERED FORWARD)
       |--Clustered Index Seek(OBJECT:(PK_Orders), SEEK:(OrderId=OrderId) LOOKUP ORDERED FORWARD)
```

`IX_Orders_CustomerId` finds the 50 matching `OrderId`s (the `Index Seek`), and the `Clustered Index
Seek ... LOOKUP` row is the key lookup itself: one extra clustered-index seek *per row* to fetch
`Status` and `OrderTotal`, joined back via `Nested Loops`. `SET STATISTICS IO ON` for this query:
**164 logical reads**.

## The index with INCLUDE

```sql
CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON Orders(CustomerId)
    INCLUDE (Status, OrderTotal)
    WITH (DROP_EXISTING = ON);
```

`INCLUDE` rather than adding `Status`/`OrderTotal` as key columns matters here: the index's sort key
stays just `CustomerId` (so it's still efficient for equality/range seeks and stays narrower than a
composite key would be), while `Status` and `OrderTotal` ride along in the leaf row only - exactly
what a plain `SELECT` list needs and nothing a seek predicate or `ORDER BY` would use them for anyway.
`WITH (DROP_EXISTING = ON)` rebuilds the existing index in place in one operation, instead of
`DROP INDEX` + `CREATE INDEX` as two.

## After: the same query, lookup gone

```sql
SELECT OrderId, Status, OrderTotal FROM Orders WHERE CustomerId = 777;
```

Actual plan:

```
SELECT [OrderId],[Status],[OrderTotal] FROM [Orders] WHERE [CustomerId]=@1
  |--Index Seek(OBJECT:(IX_Orders_CustomerId), SEEK:(CustomerId=@1) ORDERED FORWARD)
```

No `Nested Loops`, no `Clustered Index Seek ... LOOKUP` - just the one `Index Seek`. `Status` and
`OrderTotal` are now sitting right in the index's leaf row alongside `CustomerId`, so there's nothing
left to fetch from the clustered index. `SET STATISTICS IO ON`: **3 logical reads**.

## Logical-reads delta

| | Reads | Plan |
|---|---:|---|
| Before (non-covering index) | 164 | Index Seek + Key Lookup (Nested Loops) |
| After (covering index) | **3** | Index Seek only |
| Delta | **-161 reads (-98%)** | Key Lookup eliminated |

Full schema, seed data, and both `SET STATISTICS IO`/`SET STATISTICS PROFILE` checkpoints (run in
order, reproducible top to bottom) are in [query.sql](query.sql). A restorable backup of the finished
database (clustered PK + the covering `IX_Orders_CustomerId`, 100,000 rows) is saved alongside it as
`Day8Piece2.bak` - restoring it and re-running the query reproduces the same 3 logical reads.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-8/piece2

## Notes for mentor

Same setup as `day-8/piece1`: no local SQL Server or SSMS was available, so a real SQL Server 2022
container was started with Docker and driven directly with `sqlcmd`. Every number above - both the
164/3 logical-reads figures and the plan text itself - is copy-pasted from that session's real
`SET STATISTICS IO` / `SET STATISTICS PROFILE ON` output, not estimated; `query.sql`'s inline
comments quote the same numbers next to the statement that produced them, and re-running the whole
script (verified before writing this up) reproduces them exactly.

## What did I learn this session?

`WITH (DROP_EXISTING = ON)` is the part that clicked: adding `INCLUDE` columns to an existing index
doesn't mean `DROP INDEX` then `CREATE INDEX` as two separate statements (which would leave the table
with no index on `CustomerId` at all in between, and cost two rebuilds instead of one) - `DROP_EXISTING`
rebuilds the same index in place, in a single operation, while keeping the same name. The bigger idea
underneath: a non-clustered index's leaf row already carries the clustering key for free (that's how
a Key Lookup finds its way back), so `INCLUDE` only needs to add the columns the query actually reads
that *aren't* already there for free or already the seek key.

## What would break this?

- The covering index only covers this exact `SELECT` list. Add one more column to it (say,
  `CreatedAt`) and the plan goes straight back to `Index Seek + Key Lookup`, because that column
  isn't in the index's leaf row - covering is a property of a specific query, not the index in the
  abstract.
- `INCLUDE`d columns still cost write-side maintenance and page space even though they're not part of
  the seek key - a table with many wide non-key columns `INCLUDE`d "just in case" pays that cost on
  every insert/update to those columns without necessarily ever covering the query it was built for,
  if the query later changes.
- This index is only reliably worth it because `CustomerId` is evenly distributed (2,000 values,
  ~50 rows each - see `day-8/piece1`'s note on the same column). A skewed `CustomerId` with a few
  huge accounts would make the optimizer fall back to a full scan for those specific values regardless
  of what the index includes, since a scan can beat a seek-plus-many-rows once the row count gets
  large enough relative to the table.
