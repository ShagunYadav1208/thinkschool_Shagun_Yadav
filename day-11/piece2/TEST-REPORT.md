# Day 11 Piece 2 - Test Report

A full clean-room re-run of [FixedEndpointApi](FixedEndpointApi): fresh `bin`/`obj`, a brand-new SQL
Server 2022 container, a freshly seeded database, and every claim in [README.md](README.md)
independently re-checked rather than assumed.

## What was tested and the result

| # | Claim in README.md | Result |
|---|---|:---:|
| 1 | Clean build succeeds | **PASS** |
| 2 | Seeds 1,000 authors x 10,000 quotes | **PASS** |
| 3 | `EnsureCreated()` auto-creates `IX_Quotes_AuthorId` with no manual DDL, once the FK relationship is configured | **PASS** |
| 4 | `/authors-summary-before` emits exactly 1,001 SQL commands | **PASS** - exact match |
| 5 | Before plan (no index) is `Clustered Index Scan`, 117 logical reads | **PASS** - exact match, after correcting a test-procedure mistake (see below) |
| 6 | `/authors-summary-after` emits exactly 2 SQL commands, identical text to README | **PASS** - exact match |
| 7 | After plan is `Clustered Index Scan` (Authors, 9 reads) + `Hash Match Inner Join` over two `Clustered Index Scan`s (9 + 117 reads) = 135 total | **PASS** - exact match |
| 8 | The new index is *not* used by statement 2's plan even though it exists | **PASS** - reproduced; SQL Server still chose a full scan |
| 9 | Before p50/p99 multi-second | **PASS** - 4.73s / 4.99s this run vs. 4.87s-5.68s / 5.43s-6.5s originally |
| 10 | After p50/p99 double-digit-to-low-triple-digit ms | **PASS** - 155ms / 232ms this run vs. 153-177ms / 244-295ms originally |
| 11 | p99 improvement >= 10x | **PASS** - ~21.5x this run (4.99s -> 232ms), consistent with the ~18x-27x originally reported |

## A mistake I made during testing, and the fix

My first attempt to re-check claim 5 (the "before" plan) queried SQL Server **without dropping the
index first** - `EnsureCreated()` had already auto-created `IX_Quotes_AuthorId` at startup (correctly
reproducing claim 3), so that first check showed an `Index Seek`, 2 logical reads - the *after* shape,
not the *before* one. That's not a bug in `FixedEndpointApi`; it's a gap in my own test sequencing -
the "before" state requires an explicit `POST /admin/drop-index` first, exactly as the original
profiling session did. Once I called that endpoint and re-ran the same query, it correctly showed the
`Clustered Index Scan` / 117-logical-reads plan the README documents. Re-verified end to end after the
correction; recorded here so the same mistake isn't repeated without noticing it's a test-order issue,
not a product issue.

## What matched exactly vs. what varied

The **SQL text, query counts, and execution plans matched exactly**, both for the "before" state and
the "after" state, character-for-character - deterministic properties of the code, not the machine.
The **absolute latency numbers varied somewhat** (before p99 4.99s vs. originally 5.43s-6.5s; after
p99 232ms vs. originally 244-295ms) but landed within or below the originally-reported range both
times, and the **relative improvement stayed comfortably past the 10x target** (~21.5x this run). The
one plan-level surprise from the original session - the new index sitting unused in the "after"
query's plan because that query needs every row anyway - reproduced exactly as documented, not as a
fluke.

## Cleanup

The test SQL Server container (`day11p2-mssql-test`) and the API process were both stopped and
removed after this report was compiled.

## Ready to submit?

**Yes.** All 11 claims in the original README hold on an independent, from-scratch rebuild: the model
fix (real FK -> auto-index), the query fix (`Include`+`AsSplitQuery`, 2 queries flat), both execution
plans, and the >=10x p99 target were all re-verified for real, not assumed. The one hiccup during
testing was in my own verification sequencing, not in the implementation, and is called out above so
it doesn't get mistaken for a product issue on a future re-read. Nothing here blocks submission.
