# Day 9 Piece 1 - Verification on Azure SQL Database

The original [README.md](README.md) was captured against a SQL Server 2022 Docker container. This
report re-runs the exact same three anomaly pairs, unmodified, against a **real Azure SQL Database**
to check whether the results - and specifically the mechanism behind READ COMMITTED's dirty-read
prevention - hold on a managed PaaS engine instead of a full SQL Server instance.

## Environment

- **Resource**: Azure SQL Database, Basic tier (5 DTU, 2 GB), server `sql-day9p1-2444-centralindia`,
  database `Day9Piece1`.
- **Region**: `centralindia`. This subscription (Azure for Students) restricts which regions it can
  deploy to - `eastus`, `southeastasia`, and `westeurope` were all rejected with
  `RequestDisallowedByAzure`; `centralindia` was the region that succeeded.
- **Engine**: `Microsoft SQL Azure (RTM) - 12.0.2000.8`.
- **Firewall**: a single server-level rule allow-listing this machine's public IP, removed afterward.
- **Client**: no local `sqlcmd` was available on this machine either, so the same
  `mcr.microsoft.com/mssql/server:2022-latest` image used to host the original Docker tests was reused
  purely as an `sqlcmd` client (`--entrypoint tail -f /dev/null` to keep it alive without starting a
  local SQL Server), connecting out to the Azure endpoint over TLS (`-N -C`).
- **Schema**: `schema.sql`'s table/seed portion, run as-is (`CREATE DATABASE`/cross-database `USE` in
  the original script don't apply - Azure SQL Database is a single database per connection, and the
  database itself was already provisioned via `az sql db create`). All six `session-a-*.sql` /
  `session-b-*.sql` files ran completely unmodified.

## The one thing worth checking first: RCSI

```sql
SELECT name, is_read_committed_snapshot_on FROM sys.databases WHERE name = 'Day9Piece1';
-- Day9Piece1   1
```

Azure SQL Database ships with **READ COMMITTED SNAPSHOT ISOLATION (RCSI) on by default** - the
original README's "What would break this" section flagged this as the one thing likely to change if
this exercise ran on Azure instead of a plain container, since RCSI makes READ COMMITTED serve a
row-versioned snapshot instead of blocking on the writer's lock. That prediction is exactly what
happened.

## Test 1: Dirty read - same outcome, different mechanism

Phase 1 (READ UNCOMMITTED) reproduced identically - B reads the uncommitted `9999.00`:

```
A 04:53:44.576  BEGIN TRAN; UPDATE Balance = 9999.00 (uncommitted)
B 04:53:46.556  reading Balance for AccountId = 1...  -> 9999.00     <- dirty read, same as container
A 04:53:49.590  ROLLBACK TRAN
```

Phase 2 (READ COMMITTED) still avoided the dirty read - but did **not block**:

```
A 04:53:51.689  BEGIN TRAN; UPDATE Balance = 9999.00 (uncommitted)
B 04:53:53.662  attempting read (expect this to block)...  -> 100.00   <- returned INSTANTLY
B 04:53:53.662  read returned (same timestamp as the line above)
A 04:53:56.692  ROLLBACK TRAN                                          <- happens 3 seconds LATER
```

On the container, B's read only returned at the exact instant A's `ROLLBACK` completed (proof it was
blocked on A's lock). Here, B's read returns **before A has even finished rolling back** - RCSI served
it a versioned snapshot of the last-committed value instead of making it wait. No dirty read either
way, but "blocks, then reads the old value" and "never blocks, reads a version" are two different
mechanisms for the same guarantee.

## Test 2: Non-repeatable read - identical to the container

```
Phase 1 (READ COMMITTED):
A 04:54:17.383  first read  -> 100.00
B 04:54:19.356  UPDATE Balance = 500.00 (autocommit) -> committed instantly
A 04:54:22.392  second read, same transaction -> 500.00      <- non-repeatable read

Phase 2 (REPEATABLE READ):
A 04:54:24.499  first read  -> 100.00
B 04:54:26.453  attempting UPDATE Balance = 999.00 (expect this to block)...
                 <- blocks for ~3 seconds ->
A 04:54:29.505  second read, same transaction -> 100.00       <- no non-repeatable read
B 04:54:29.518  UPDATE completed - only after A committed
```

REPEATABLE READ is lock-based, not affected by RCSI (RCSI only changes what READ COMMITTED does) - so
this behaves exactly like the container test, blocking included.

## Test 3: Phantom read - identical to the container

```
Phase 1 (REPEATABLE READ):
A 04:54:47.357  first count  -> 3
B 04:54:49.383  INSERT AccountId = 4, Balance = 150.00 (autocommit) -> committed
A 04:54:52.363  second count, same transaction -> 4          <- phantom row appeared

Phase 2 (SERIALIZABLE):
A 04:54:54.479  first count  -> 3
B 04:54:56.552  attempting INSERT AccountId = 4 (expect this to block)...
                 <- blocks for ~3 seconds ->
A 04:54:59.493  second count, same transaction -> 3           <- no phantom
B 04:54:59.587  INSERT completed - only after A committed
```

SERIALIZABLE is also lock-based (range/key-range locks), unaffected by RCSI - identical result to the
container, blocking included.

## Updated summary table

| Anomaly | Reproduced at | Prevented at | Mechanism on Azure SQL Database |
|---|---|---|---|
| Dirty read | READ UNCOMMITTED | READ COMMITTED | **No blocking** - RCSI serves a row-versioned last-committed snapshot instantly |
| Non-repeatable read | READ COMMITTED | REPEATABLE READ | Blocking (lock-based) - identical to the Docker container |
| Phantom read | REPEATABLE READ | SERIALIZABLE | Blocking (lock-based) - identical to the Docker container |

## Conclusion

All three anomalies reproduce and all three preventions hold on real Azure SQL Database, with one
genuine behavioral difference: dirty-read prevention under READ COMMITTED is non-blocking on Azure SQL
Database (RCSI, on by default) versus blocking on a stock SQL Server instance (RCSI off by default).
Anyone porting this exercise's "the reader blocks for ~3 seconds" evidence to Azure SQL Database
specifically should expect the *value* guarantee to hold but the *blocking* evidence not to appear.

## Cleanup

The resource group (`rg-day9-piece1-test-centralindia`), SQL server, database, and firewall rule were
all deleted after this report was captured, to avoid ongoing cost on the underlying subscription. This
report is the durable record of what was observed; the Azure resources themselves no longer exist.
