# Day 12 Piece 1 - Test Report

A full clean-room re-run of [CqrsLiteApi](CqrsLiteApi): fresh `bin`/`obj`, a freshly reseeded SQLite
database, and every claim in [README.md](README.md) independently re-checked rather than assumed.

## What was tested and the result

| # | Claim in README.md | Result |
|---|---|:---:|
| 1 | Clean build succeeds | **PASS** |
| 2 | Startup creates schema, auto-creates `IX_Quotes_AuthorId`, seeds 2 authors | **PASS** |
| 3 | `POST /quotes` with valid data returns `201 {"quoteId":N}` | **PASS** - exact match, both quotes |
| 4 | `POST /quotes` with empty text returns `400` FluentValidation error | **PASS** - exact match |
| 5 | `POST /quotes` with a nonexistent author returns `404 {"error":"Author 999 does not exist."}` | **PASS** - exact match |
| 6 | `GET /authors/1/feed` returns both quotes, newest first, with `postedAgoDisplay` | **PASS** - exact match |
| 7 | `GET /authors/2/feed` (zero quotes) returns `200` with an empty `quotes` array, not an error | **PASS** - exact match |
| 8 | `GET /authors/999/feed` returns `404` | **PASS** |
| 9 | The read query compiles to exactly ONE SQL round trip regardless of quote count | **PASS** - single `LEFT JOIN` + correlated `COUNT` subquery, verified via fresh `LogTo` capture |
| 10 | Read query orders by `QuoteId` (not `CreatedAt`), the fix for the SQLite `DateTimeOffset` `ORDER BY` limitation documented in the README | **PASS** - confirmed in the captured SQL: `ORDER BY "a0"."AuthorId", "q0"."QuoteId" DESC` |

## What matched exactly

Every response body, status code, and the SQL text itself matched the README character-for-character
on a completely fresh database and process - no drift, no flakiness. Unlike the SQL Server / load-test
exercises from Day 8-11, this piece has no timing-sensitive claims (SQLite, single requests, no
concurrent load), so there's no "ran a bit slower this time" caveat to make here - either the
JSON/SQL matches or it doesn't, and it did, on every one of the 10 checks above.

## Process note

Learned from the Day 11 test reports: confirmed the real PID via `netstat -ano | grep 5301` after
every restart, rather than trusting bash's `$!`, before running any check against it. No stale-process
mixups this time.

## Cleanup

The API process was stopped after this report was compiled; no container or other external resource
was used for this piece (SQLite is file-based, and the seed script deletes and recreates
`cqrslite.db` on every startup, so no manual database cleanup was needed either).

## Ready to submit?

**Yes.** All 10 claims in the original README hold exactly on an independent, from-scratch rebuild:
every write-path response, every read-path response, and the read model's single-query SQL shape
(including the SQLite `ORDER BY` fix) all reproduced without any deviation. Nothing here blocks
submission.
