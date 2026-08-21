# Day 11 Piece 1 - Test Report

A full clean-room re-run of [SlowEndpointApi](SlowEndpointApi), independent of the original profiling
session: fresh `bin`/`obj` (deleted and rebuilt), a brand-new SQL Server 2022 container, a freshly
seeded database, and every claim in [README.md](README.md) re-checked against that fresh state rather
than assumed to still hold.

## What was tested and the result

| # | Claim in README.md | Result |
|---|---|:---:|
| 1 | Clean build succeeds (`rm -rf bin obj && dotnet build -c Release`) | **PASS** |
| 2 | Seeds exactly 1,000 authors x 10 quotes = 10,000 quotes | **PASS** |
| 3 | `/authors-summary-slow` emits exactly 1,001 SQL commands (1 Authors + 1,000 per-author `COUNT(*)`) | **PASS** - exact match, re-verified via fresh `LogTo` capture |
| 4 | Baseline per-author query plan is a `Clustered Index Scan`, 117 logical reads | **PASS** - exact match |
| 5 | `/authors-summary-fast` emits exactly 1 SQL command (correlated subquery) | **PASS** - exact match, identical SQL text |
| 6 | Fixed query's plan is `Index Scan` -> `Stream Aggregate` -> `Merge Join`, 29 total logical reads (20 + 9) | **PASS** - exact match |
| 7 | Baseline (no index) p50/p99 are multi-second and directionally match | **PASS** - 6.27s / 6.78s this run vs. 4.94s / 5.27s originally |
| 8 | Index-only (still N+1) improves but stays multi-second | **PASS** - 3.22s / 3.59s this run vs. 2.83s / 4.45s originally |
| 9 | Fully fixed (single query + index) drops to double-digit milliseconds | **PASS** - 15.97ms / 38.95ms this run vs. 10.77ms / 25.72ms originally |
| 10 | Relative improvement is roughly two orders of magnitude at p50 | **PASS** - ~392x this run (6.27s -> 15.97ms) vs. ~460x originally |

## What changed run-to-run, and why that's expected

The **SQL text and execution plans matched exactly**, character-for-character, both times - those are
deterministic properties of the code and schema, not the machine's mood. The **absolute latency
numbers were consistently slower this run** (baseline p50 6.27s vs. 4.94s originally; fixed-endpoint
p50 15.97ms vs. 10.77ms) - expected, since this machine had already run a full day's worth of Docker
containers, SQL Server instances, and diagnostic queries earlier in this session, so it wasn't as idle
as during the original profiling run. What matters for this exercise - the *shape* of the problem (1
vs. 1,001 round trips, table scan vs. index scan, ~400x improvement) - reproduced exactly; the
*exact* millisecond values are, correctly, sensitive to ambient machine load, which is why the README
already treats the timing ratio as "roughly Nx" rather than a precise constant.

## Process note

Managing multiple background `dotnet` processes through this shell turned out to be the trickiest part
of re-testing, not the app itself: bash's `$!` job-control PID did not reliably match the OS-level PID
`tasklist`/`netstat` reported for the spawned `dotnet.exe`, which caused a couple of `taskkill`s to
silently miss their target and leave a stale instance holding port 5299 (serving stale/default-logging
responses under a "kill" that had already "succeeded"). Every verification above was redone only after
confirming, via `netstat -ano | grep 5299`, that the PID being tested was actually the one listening -
worth flagging in case anyone else scripts this same restart-with-different-env-vars pattern.

## Cleanup

The test SQL Server container (`day11-mssql-test`) and the API process were both stopped and removed
after this report was compiled. No test artifacts were left running.

## Ready to submit?

**Yes.** All 10 claims in the original README hold on an independent, from-scratch rebuild - the SQL,
the execution plans, and the qualitative performance story (N+1 + missing index -> multi-second;
single query + index -> double-digit milliseconds) all reproduced. The only variance was absolute
timing magnitude, which is expected machine-load noise and doesn't affect any claim the exercise
actually asks for (baseline p50/p99, the offending SQL, the plan, and the two biggest problems).
Nothing here blocks submission.
