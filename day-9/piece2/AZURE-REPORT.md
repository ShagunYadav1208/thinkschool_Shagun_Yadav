# Day 9 Piece 2 - Verification on Azure SQL Database

The original [README.md](README.md) captured a deadlock and its fix against a SQL Server 2022 Docker
container, using trace flag 1222 to write the deadlock graph to the error log. This report re-runs
the exact same `session-a-*.sql` / `session-b-*.sql` scripts, unmodified, against a **real Azure SQL
Database** - and specifically checks whether the trace-flag-1222 capture method still works there.

## Environment

- **Resource**: Azure SQL Database, Basic tier (5 DTU, 2 GB), server `sql-day9p2-6191`, database
  `Day9Piece2`, region `centralindia` (same region constraint as the Piece 1 report - this
  subscription only allows a specific subset of regions).
- **Firewall**: a single server-level rule allow-listing this machine's public IP, removed afterward.
- **Client**: same approach as the Piece 1 Azure report - the `mcr.microsoft.com/mssql/server:2022-latest`
  image running only as an `sqlcmd` client (`--entrypoint tail -f /dev/null`), connecting out to the
  Azure endpoint over TLS.
- **Schema**: the `Accounts` table + seed rows from `schema.sql`, created directly against the
  already-provisioned database (same reasoning as Piece 1: Azure SQL Database doesn't support
  `CREATE DATABASE`/cross-database `USE` the way a full instance does). All four
  `session-a-*.sql` / `session-b-*.sql` files ran completely unmodified.

## The thing that broke first: trace flag 1222 doesn't work on Azure SQL Database

```
DBCC TRACEON(1222, -1);
-- Msg 2571, Level 14, State 3: User 'dbo' does not have permission to run DBCC TRACEON.

EXEC sp_readerrorlog 0, 1, N'deadlock';
-- Msg 2812, Level 16, State 62: Could not find stored procedure 'sp_readerrorlog'.
```

Both fail outright. Azure SQL Database is a managed PaaS service - there's no server-level
`DBCC TRACEON`, and no accessible `ERRORLOG` file or `sp_readerrorlog` to read one back from, because
there's no dedicated instance underneath a single database to have one. The original exercise's other
allowed method - Extended Events - is the one that actually works here, but even that needed an
adjustment:

```sql
-- This is what the original exercise (and on-prem SQL Server) would reach for first:
CREATE EVENT SESSION CaptureDeadlocks ON DATABASE
    ADD EVENT sqlserver.xml_deadlock_report ADD TARGET package0.ring_buffer;
-- Msg 25743: The event 'sqlserver.xml_deadlock_report' is not available for Azure SQL Database.
```

`system_health` also isn't running by default on this database (`sys.database_event_sessions` came
back empty), unlike a full SQL Server instance where it always is. Azure SQL Database has its own
database-scoped equivalent event, `sqlserver.database_xml_deadlock_report`, found by searching
`sys.dm_xe_objects` for `%deadlock%`:

```sql
CREATE EVENT SESSION CaptureDeadlocks ON DATABASE
    ADD EVENT sqlserver.database_xml_deadlock_report
    ADD TARGET package0.ring_buffer
    WITH (MAX_MEMORY = 4096 KB, STARTUP_STATE = ON);
ALTER EVENT SESSION CaptureDeadlocks ON DATABASE STATE = START;
```

This one works. Full XML in [deadlock-graph-azure.txt](deadlock-graph-azure.txt).

## The repro itself: identical outcome

Same scripts, same circular wait, same victim mechanism:

```
A 05:07:56.222  UPDATE AccountId = 1 (locks row 1)
B 05:07:56.223  UPDATE AccountId = 2 (locks row 2)
A 05:07:59.226  attempting UPDATE AccountId = 2 (needs row 2)...   <- blocks, B holds it
B 05:07:59.226  attempting UPDATE AccountId = 1 (needs row 1)...   <- blocks, A holds it
                 <- circular wait; Azure's lock monitor detects it ~1.5s later ->
A 05:08:00.759  got row 2, committing
B: Msg 1205, Level 13, State 72 - chosen as the deadlock victim
```

```
Msg 1205, Level 13, State 72, Server sql-day9p2-6191, Line 7
Transaction (Process ID 80) was deadlocked on lock resources with another process and has been
chosen as the deadlock victim. Rerun the transaction.
```

Same session (B) was chosen as victim as in the container run - not something to read into, since
victim selection depends on rollback cost/priority, which happened to land the same way both times.
Detection took ~1.5 seconds here versus ~3.3 seconds on the container - both are just the adaptive
lock-monitor interval, not a functional difference.

## The deadlock graph: same substance, different shape

The essential two `keylock`-under-`xactlock` entries are structurally identical to the container's
graph (each process owns exactly what the other is waiting for), but three concrete details differ:

| | Docker container | Azure SQL Database |
|---|---|---|
| Deadlock event name | `xml_deadlock_report` (server-scoped) | `database_xml_deadlock_report` (database-scoped) |
| Lock resource wrapper | plain `keylock` | `xactlock` wrapping a `keylock` (`UnderlyingResource`) |
| `waitresource` format | `KEY: 5:72057594045726720 (...)` | `XACT: 5:1119:0 KEY: 5:72057594047823872 (...)` |
| `objectname` | `Day9Piece2.dbo.Accounts` | a database GUID + `.dbo.Accounts` (e.g. `430b9c58-...dbo.Accounts`) |
| Waiter's `lockMode` at the moment of detection | `X` on both sides | `S` on both sides (owners still show `X`) |

The `XACT:` wrapper and GUID-based object name are visible artifacts of Azure SQL Database's
underlying multi-tenant lock manager (a single physical server hosts many logical databases, so
locks are tracked per-database transaction, not just per-object) - the exercise's core teaching point
(two processes, each holding what the other wants) is unaffected by any of this; only the graph's
*packaging* differs. The waiter `lockMode="S"` versus the container's `"X"` is a genuine difference
worth flagging honestly rather than explaining away with confidence - it may reflect a different
internal lock-request sequence Azure SQL Database uses for an `UPDATE`'s read-then-write phases, but
this report didn't dig deeply enough to state that as fact.

## The fix: same outcome, non-deterministic detail

Same reordering (`session-b-fixed.sql` touches `AccountId = 1` before `AccountId = 2`, matching
`session-a-fixed.sql`'s order) - no deadlock, both transactions commit, final balances unchanged
(100.00 / 100.00):

```
B 05:08:48.775  UPDATE AccountId = 1 (locks row 1 FIRST...)   <- wins the race for row 1
A 05:08:48.776  UPDATE AccountId = 1 (locks row 1...)         <- blocks; B already holds it
B 05:08:51.797  UPDATE AccountId = 2 (locks row 2 second)     <- free; commits
A 05:08:54.924  UPDATE AccountId = 2 (locks row 2)             <- unblocked once B committed; free now
A 05:08:54.925  committing
```

One honest difference from the container run worth calling out: on the container, **A** won the race
for row 1 and B blocked; here, **B** won and A blocked. Both are the same fix working correctly - the
point of consistent lock ordering isn't "the same session always wins," it's "whichever session gets
there first proceeds, and the other waits instead of deadlocking." Which one wins a near-simultaneous
race is inherently non-deterministic and was never part of the guarantee.

## Conclusion

The deadlock repro, the victim message, and the fix all reproduce identically on Azure SQL Database.
The one thing that doesn't carry over unmodified is the *capture method*: trace flag 1222 and
`sp_readerrorlog` are both unavailable on Azure SQL Database (no server-level access, no accessible
error log), and even the fallback Extended Events approach needs the Azure-specific event name
(`database_xml_deadlock_report` instead of `xml_deadlock_report`) since `system_health` isn't running
by default on a Basic-tier database the way it always is on a full instance.

## Cleanup

The Extended Events session was stopped and dropped, then the resource group
(`rg-day9-piece2-test`), SQL server, database, and firewall rule were all deleted after this report
was captured, to avoid ongoing cost on the underlying subscription.
