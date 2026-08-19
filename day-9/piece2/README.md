# Day 9 - Reproduce and resolve a deadlock

Same setup as [day-9/piece1](../piece1): no local SQL Server was available, so a real SQL Server 2022
container was started via Docker, and the deadlock below was forced for real between two concurrent
`sqlcmd` connections - not narrated. Trace flag 1222 (`DBCC TRACEON(1222, -1)`, see
[schema.sql](schema.sql)) was enabled server-wide before the repro so SQL Server would write the
actual deadlock graph to its error log, which was then pulled out directly.

`Accounts(AccountId INT PRIMARY KEY, Balance DECIMAL(10,2))`, seeded with two rows at 100.00, models
a two-account transfer. Session A always runs "transfer 10.00 from Account 1 to Account 2." Session B
always runs "transfer 10.00 from Account 2 to Account 1" - the classic setup where two transactions
touch the same two resources in opposite order.

## The repro: two-session scripts

[session-a-deadlock.sql](session-a-deadlock.sql):
```sql
BEGIN TRAN;
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 1;   -- locks row 1
WAITFOR DELAY '00:00:03';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 2;   -- then needs row 2
COMMIT TRAN;
```

[session-b-deadlock.sql](session-b-deadlock.sql):
```sql
BEGIN TRAN;
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 2;   -- locks row 2
WAITFOR DELAY '00:00:03';
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 1;   -- then needs row 1
COMMIT TRAN;
```

Both scripts are launched at the same instant. Each grabs its own row first (A: row 1, B: row 2,
within milliseconds of each other), holds it for 3 seconds, then reaches for the *other* row - which
the other session is now holding. Real captured timestamps:

```
A 04:28:16.702  UPDATE AccountId = 1 (locks row 1)
B 04:28:16.706  UPDATE AccountId = 2 (locks row 2)
A 04:28:19.694  attempting UPDATE AccountId = 2 (needs row 2)...    <- blocks, B holds it
B 04:28:19.694  attempting UPDATE AccountId = 1 (needs row 1)...    <- blocks, A holds it
                 <- circular wait: SQL Server's lock monitor detects it ~3.3s later ->
A 04:28:23.031  got row 2, committing
B: Msg 1205, Level 13, State 51 - chosen as the deadlock victim
```

## The victim message

```
Msg 1205, Level 13, State 51, Server 2ab6bb34960a, Line 7
Transaction (Process ID 77) was deadlocked on lock resources with another process and has been
chosen as the deadlock victim. Rerun the transaction.
```

Session B (spid 77) was killed; its transaction was automatically rolled back. Session A (spid 78)
proceeded and committed.

## The deadlock graph

Pulled from the real SQL Server error log (trace flag 1222) - full text in
[deadlock-graph.txt](deadlock-graph.txt), also retrievable live with
`EXEC sp_readerrorlog 0, 1, N'deadlock-list';`. The essential shape:

```
deadlock-list
 deadlock victim=processe91fb4108
 process-list
  process id=processe91fb4108 spid=77 waitresource=KEY:...(8194443284a0) lockMode=X
      inputbuf: ...UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 2...
                   ...UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 1...   <- Session B
  process id=processe92177c28 spid=78 waitresource=KEY:...(61a06abd401c) lockMode=X
      inputbuf: ...UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 1...
                   ...UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 2...   <- Session A
 resource-list
  keylock ... id=locke80566780   owner=processe92177c28 (A)   waiter=processe91fb4108 (B)
  keylock ... id=locke80567700   owner=processe91fb4108 (B)   waiter=processe92177c28 (A)
```

The two `keylock` entries are the whole story: the first is Account 2's row, owned by A (waiting), the
second is Account 1's row, owned by B (waiting) - each process owns the exact lock the other one is
waiting for. That's the cycle.

## The fix: consistent lock ordering

[session-a-fixed.sql](session-a-fixed.sql) is unchanged - it already touched the lower `AccountId`
first. [session-b-fixed.sql](session-b-fixed.sql) keeps the same transfer (Account 2 still ends up
-10.00, Account 1 still ends up +10.00) but reorders *which UPDATE runs first* to also touch
`AccountId = 1` before `AccountId = 2`, regardless of which account is logically the "source":

```sql
-- session-b-fixed.sql
BEGIN TRAN;
UPDATE Accounts SET Balance = Balance + 10.00 WHERE AccountId = 1;   -- lower id first, even though it's the destination
WAITFOR DELAY '00:00:03';
UPDATE Accounts SET Balance = Balance - 10.00 WHERE AccountId = 2;
COMMIT TRAN;
```

Real captured run - no deadlock, both transactions commit, final balances unchanged (100.00 / 100.00,
since the two transfers net to zero):

```
A 04:29:34.980  UPDATE AccountId = 1 (locks row 1, the lower id)
B 04:29:34.984  UPDATE AccountId = 1 (locks row 1 FIRST...)          <- blocks; A already holds it
A 04:29:37.995  UPDATE AccountId = 2 (locks row 2)                   <- free, A never contended for it
A 04:29:37.995  committing                                          <- releases row 1
B 04:29:41.021  UPDATE AccountId = 2 (locks row 2 second)            <- B's row-1 wait just ended; row 2 is free
B 04:29:41.021  committing
```

B still waits ~3 seconds for A's lock on row 1 - that's ordinary blocking, not a deadlock. By the time
B gets row 1 and moves on to row 2, A has already committed and released it, so B's second `UPDATE`
sails through. **One line on why it works:** a deadlock needs a *cycle* of "I hold what you want, you
hold what I want" - if every transaction acquires the same two locks in the same order, the second
transaction to arrive can only ever be blocked waiting for the *first* lock in that order, never
waiting on a lock the first transaction is itself waiting on, so the wait-for graph can no longer
close into a cycle.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-9/piece2

## Notes for mentor

Trace flag 1222 was enabled with `DBCC TRACEON(1222, -1)` (server-wide) in `schema.sql` before running
the repro; the graph in `deadlock-graph.txt` is copy-pasted straight from the container's real
`/var/opt/mssql/log/errorlog` (retrieved via `docker cp`, cross-checked live with
`sp_readerrorlog 0, 1, N'deadlock-list'`), with only each process's internal engine call-stack
("stackFrames" - raw memory addresses, not part of the deadlock's substance) trimmed for readability.
Every identifying field (process ids, spids, lock resources, isolation levels, and both sessions'
actual `inputbuf` SQL text) is real, unedited output. `Day9Piece2.bak` is a backup of the database as
left after the fix run (both balances back at 100.00).

## What did I learn this session?

The part that clicked: the fix doesn't change *what* either transaction does to the data - Account 1
still nets to +10/-10 across the two transfers exactly as before - it only changes the *order* the two
`UPDATE` statements run in within Session B. Deadlocks are a property of lock **acquisition order**,
completely independent of the business logic's own "direction." That's why the fix is called
"consistent lock ordering" rather than "fix the logic" - the logic was never wrong.

## What would break this?

- This fix only works if *every* transaction touching these two rows agrees on the same ordering rule
  (e.g. "always touch the lower `AccountId` first"). A third code path added later that updates
  `AccountId = 2` before `AccountId = 1` - even if it's logically unrelated to "transfers" - would
  reintroduce the exact same cycle.
- A `WHERE` clause that touches an unpredictable set of rows (say, `UPDATE Accounts SET ... WHERE
  Balance > @threshold`, where the matching rows depend on runtime data) can't be given a fixed lock
  order by inspection - two such transactions could still deadlock unpredictably depending on what
  rows happen to match on a given run.
- Consistent lock ordering prevents *this* cycle but not deadlocks between more than two resources (a
  three-way cycle: A waits on B, B waits on C, C waits on A) - that needs the same discipline applied
  transitively across every resource all transactions might touch, not just pairwise.
