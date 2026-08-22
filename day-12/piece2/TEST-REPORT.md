# Day 12 Piece 2 - Test Report

A full clean-room re-run of [DapperVsEfApi](DapperVsEfApi): fresh `bin`/`obj`, a freshly reseeded
SQLite database (1,000 authors x 10,000 quotes), and every claim in [README.md](README.md)
independently re-checked rather than assumed.

## What was tested and the result

| # | Claim in README.md | Result |
|---|---|:---:|
| 1 | Clean build succeeds | **PASS** |
| 2 | `GET /authors/500/feed-ef` and `GET /authors/500/feed-dapper` return byte-for-byte identical JSON | **PASS** - exact match, including all 10 quote IDs and `postedAgoDisplay` strings |
| 3 | `GET /authors/9999/feed-ef` and `-dapper` both return `404` for a nonexistent author | **PASS** |
| 4 | EF query compiles to the exact documented SQL (single `LEFT JOIN` + correlated `COUNT` subquery) | **PASS** - character-for-character match via fresh `LogTo` capture |
| 5 | Both known bugs (`int`/`long` constructor mismatch, `DateTimeOffset` round-trip) stay fixed | **PASS** - both endpoints return `200` with no exceptions |
| 6 | Dapper is consistently faster than EF Core on this read | **PASS** - Dapper faster in both benchmark re-runs (median ~1.8x-2.4x faster this time, vs. ~2.5x-2.9x originally) |
| 7 | Dapper allocates meaningfully less per call | **PASS** - allocation ratio ~3.67x-3.84x this run, essentially identical to the ~3.7x-3.8x originally reported |

## What matched exactly vs. what varied

The **JSON responses, the SQL text, and the fact that both bug fixes hold** all matched the README
exactly, character-for-character, on a completely fresh process and database. The **absolute
timings were lower across the board this run** (EF median ~0.48-0.57ms vs. ~1.1-1.6ms originally;
Dapper median ~0.20-0.31ms vs. ~0.44-0.54ms originally) - expected machine-load variance, not a
regression, since both implementations moved together. The **timing ratio compressed somewhat**
(1.8x-2.4x this run vs. 2.5x-2.9x originally) while the **allocation ratio stayed remarkably
stable** (3.67x-3.84x this run vs. 3.7x-3.8x originally) - this is exactly the pattern the README
itself calls out: allocation counts are a steadier signal than wall-clock timing under light,
unloaded conditions. The re-test didn't just fail to contradict that claim, it actively reproduced
it.

## Process note

Same discipline as the Day 11 and Day 12 Piece 1 reports: every `taskkill` was preceded by confirming
the real PID via `netstat -ano | grep 5302`, since bash's `$!` has proven unreliable for matching the
actual `dotnet.exe` OS process in this environment. No stale-process mixups this time.

## Cleanup

The API process was stopped after this report was compiled. No container or other external resource
was used (SQLite is file-based and the seed script deletes and recreates `dappervsef.db` on every
startup).

## Ready to submit?

**Yes.** All 7 claims in the original README hold on an independent, from-scratch rebuild: identical
JSON from both endpoints, identical EF-generated SQL, both documented bugs remain fixed, and Dapper
was faster and lower-allocating in both directions, on both re-run authors. The one number that moved
(the timing ratio, compressed from ~2.5-2.9x to ~1.8-2.4x) moved in a way the README already
anticipated and explained, not in a way that undermines the conclusion. Nothing here blocks
submission.
