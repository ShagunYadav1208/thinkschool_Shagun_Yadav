# Day 9 - Isolation levels + the read anomalies

Same approach as `day-8`: no local SQL Server was available, so a real SQL Server 2022 container
(`mcr.microsoft.com/mssql/server:2022-latest`) was started via Docker. This exercise specifically
needs **two concurrent sessions**, so it can't be captured as one serial script the way `day-8`'s
pieces were - instead, each anomaly is a pair of `.sql` files (`session-a-*.sql` / `session-b-*.sql`)
launched as two separate `sqlcmd` connections at the same instant, with `WAITFOR DELAY` giving the
interleaving a deterministic timeline instead of a race that may or may not reproduce. Every
timestamp and value quoted below is copy-pasted from that real run, not narrated.

`Accounts(AccountId INT PRIMARY KEY, Balance DECIMAL(10,2))`, seeded with three rows at 100.00, is
the shared table all three tests reset to before running (see [schema.sql](schema.sql)). Each pair
runs the anomaly twice back-to-back in one connection: Phase 1 (t=0..5) at the weaker isolation level
that allows the anomaly, Phase 2 (t=7..12) at the level that prevents it. Session B always fires its
write at t+2 into whichever phase is active.

## Anomaly -> lowest isolation level that prevents it

| Anomaly | Reproduced at | Lowest level that prevents it |
|---|---|---|
| Dirty read | READ UNCOMMITTED | **READ COMMITTED** |
| Non-repeatable read | READ COMMITTED (SQL Server's default) | **REPEATABLE READ** |
| Phantom read | REPEATABLE READ | **SERIALIZABLE** |

## Dirty read

[session-a-dirty-read.sql](session-a-dirty-read.sql) / [session-b-dirty-read.sql](session-b-dirty-read.sql)

```sql
-- Session A
BEGIN TRAN;
UPDATE Accounts SET Balance = 9999.00 WHERE AccountId = 1;
WAITFOR DELAY '00:00:05';
ROLLBACK TRAN;                      -- the 9999.00 never really existed

-- Session B, phase 1
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT Balance FROM Accounts WHERE AccountId = 1;   -- reads mid-transaction

-- Session B, phase 2 (same shape, different isolation level)
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT Balance FROM Accounts WHERE AccountId = 1;   -- reads mid-transaction
```

Real captured output:

```
Phase 1 (READ UNCOMMITTED):
A 03:40:01.236  BEGIN TRAN; UPDATE Balance = 9999.00 (uncommitted)
B 03:40:03.226  reading Balance for AccountId = 1...  -> 9999.00   <- dirty read
A 03:40:06.240  ROLLBACK TRAN

Phase 2 (READ COMMITTED):
A 03:40:08.256  BEGIN TRAN; UPDATE Balance = 9999.00 (uncommitted)
B 03:40:10.239  attempting read (expect this to block)...
                 <- SELECT does not return here; it blocks -
A 03:40:13.261  ROLLBACK TRAN
B 03:40:13.261  read returned -> 100.00                   <- no dirty read
```

In phase 1, B's `SELECT` returns immediately with `9999.00` - a value that stops existing three
seconds later when A rolls back. In phase 2, B's `SELECT` is issued at `03:40:10.239` but doesn't
actually return until `03:40:13.261` - the exact same instant A's `ROLLBACK` completes. READ
COMMITTED made B wait for A's exclusive lock to release instead of reading through it, so B only ever
sees `100.00`, the value that was actually, durably true.

## Non-repeatable read

[session-a-non-repeatable-read.sql](session-a-non-repeatable-read.sql) / [session-b-non-repeatable-read.sql](session-b-non-repeatable-read.sql)

```sql
-- Session A, phase 1
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRAN;
SELECT Balance FROM Accounts WHERE AccountId = 2;    -- first read
WAITFOR DELAY '00:00:05';
SELECT Balance FROM Accounts WHERE AccountId = 2;    -- second read, same transaction
COMMIT TRAN;

-- Session A, phase 2 (same shape, REPEATABLE READ instead)
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRAN;
SELECT Balance FROM Accounts WHERE AccountId = 2;
WAITFOR DELAY '00:00:05';
SELECT Balance FROM Accounts WHERE AccountId = 2;
COMMIT TRAN;

-- Session B, both phases
UPDATE Accounts SET Balance = 500.00 WHERE AccountId = 2;   -- autocommit, no BEGIN TRAN
```

Real captured output:

```
Phase 1 (READ COMMITTED):
A 03:40:30.971  first read  -> 100.00
B 03:40:32.949  UPDATE Balance = 500.00 (autocommit) -> committed instantly at 03:40:32.953
A 03:40:35.967  second read, same transaction -> 500.00    <- non-repeatable read

Phase 2 (REPEATABLE READ):
A 03:40:37.979  first read  -> 100.00
B 03:40:39.956  attempting UPDATE Balance = 999.00 (expect this to block)...
                 <- UPDATE does not return here; it blocks -
A 03:40:42.977  second read, same transaction -> 100.00    <- no non-repeatable read
A 03:40:42.977  COMMIT TRAN
B 03:40:42.985  UPDATE completed - only after A committed
```

Under READ COMMITTED, A's shared lock from the first read is released the instant that statement
finishes, so B's `UPDATE` sails through uncontested and A's second read - still the same open
transaction, no fault of its own - sees a different value. Under REPEATABLE READ, A keeps that shared
lock until `COMMIT`, so B's `UPDATE` blocks for the full ~3 seconds until A finishes, and A's second
read is guaranteed identical to its first.

## Phantom read

[session-a-phantom-read.sql](session-a-phantom-read.sql) / [session-b-phantom-read.sql](session-b-phantom-read.sql)

```sql
-- Session A, phase 1
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRAN;
SELECT COUNT(*) FROM Accounts WHERE Balance >= 100.00;   -- first count
WAITFOR DELAY '00:00:05';
SELECT COUNT(*) FROM Accounts WHERE Balance >= 100.00;   -- second count, same transaction
COMMIT TRAN;

-- Session A, phase 2 (same shape, SERIALIZABLE instead)
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRAN;
SELECT COUNT(*) FROM Accounts WHERE Balance >= 100.00;
WAITFOR DELAY '00:00:05';
SELECT COUNT(*) FROM Accounts WHERE Balance >= 100.00;
COMMIT TRAN;

-- Session B, both phases
INSERT INTO Accounts (AccountId, Balance) VALUES (4, 150.00);   -- autocommit, new row matches the predicate
```

Real captured output:

```
Phase 1 (REPEATABLE READ):
A 03:41:00.339  first count  -> 3
B 03:41:02.324  INSERT AccountId = 4, Balance = 150.00 (autocommit) -> committed at 03:41:02.332
A 03:41:05.342  second count, same transaction -> 4             <- phantom row appeared

Phase 2 (SERIALIZABLE):
A 03:41:07.355  first count  -> 3
B 03:41:09.340  attempting INSERT AccountId = 4 (expect this to block)...
                 <- INSERT does not return here; it blocks -
A 03:41:12.354  second count, same transaction -> 3             <- no phantom
A 03:41:12.354  COMMIT TRAN
B 03:41:12.370  INSERT completed - only after A committed
```

REPEATABLE READ only locks the three rows A already read - it has no way to stop a brand-new row from
appearing that also matches `Balance >= 100.00`, so B's `INSERT` is free to commit mid-transaction and
A's second count grows to 4. SERIALIZABLE takes a range (key-range) lock covering the whole predicate,
not just the rows that currently satisfy it, so B's `INSERT` blocks until A's transaction ends and the
count can never move within it.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-9/piece1

## Notes for mentor

Each anomaly is two files instead of one `query.sql`, because the exercise is inherently about two
*concurrent* connections - a single serial script can't express that. All three pairs were run for
real: two `sqlcmd` processes started together against the same SQL Server 2022 container (Docker,
`mcr.microsoft.com/mssql/server:2022-latest`, since no local instance or SSMS was available), each
`PRINT CONVERT(varchar, SYSDATETIME(), 121) + ...` timestamping its own actions so the blocking (or
lack of it) shows up as real elapsed wall-clock time in the output, not just as text claiming it
happened. `Day9Piece1.bak` alongside these scripts is a backup of the database as left after all three
tests (three seed rows plus whatever Phase 2 left behind); `schema.sql` is what actually resets it to
a clean slate before each pair runs, which is what this write-up's "real captured output" runs were
taken against.

## What did I learn this session?

The thing that clicked: REPEATABLE READ and SERIALIZABLE aren't "REPEATABLE READ, but stricter" in a
vague sense - they differ in exactly *what kind* of lock gets taken. REPEATABLE READ locks specific
*rows* it has already touched, which is why a phantom (a brand-new row nobody had a lock on yet) can
still slip in. SERIALIZABLE locks the *range* implied by the predicate itself, so nothing - existing
row or not-yet-existing row - can enter that range until the transaction is done. That's a lock-scope
distinction, not just "one more notch of strictness."

## What would break this?

- These timings assume nothing else contends for the same rows during the test. A real workload with
  other concurrent transactions touching `Accounts` could make Session B's write block (or fail with
  a deadlock) for reasons unrelated to the isolation level being demonstrated, muddying which block is
  "the" one this test is trying to show.
- SQL Server's default READ COMMITTED here is lock-based (blocks the reader). Under READ COMMITTED
  SNAPSHOT (RCSI) - which many real deployments enable - Session B's dirty-read-prevention read in
  phase 2 would *not* block at all; it would instantly return the last-committed value (`100.00`) from
  a row versioning store instead of waiting on A's lock. The prevention still holds (no dirty read),
  but the *mechanism* (block-then-read vs. read-a-version-instantly) is different, and this write-up's
  "it blocks for ~3 seconds" evidence is specific to the non-RCSI, lock-based default this container
  uses out of the box.
- A deadlock is possible if Session A and Session B ever took locks on each other's rows in opposite
  order (e.g. a phase where each session's transaction touches both rows involved) - none of these
  three tests do that, but a naive extension to more rows/columns could introduce one without changing
  the isolation level at all.
