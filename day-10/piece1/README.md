# Day 10 - EF Core change tracker + AsNoTracking

A runnable console app ([ChangeTrackerBenchmark](ChangeTrackerBenchmark)), not a description - `dotnet run -c Release`
seeds 10,000 rows into a real SQLite database, then demonstrates identity resolution, whether entities
are actually tracked, and measures the read-path cost of the change tracker with `Stopwatch` and
`GC.GetAllocatedBytesForCurrentThread()`. Same stack as the rest of this repo's EF Core pieces
(`net10.0`, EF Core `10.0.10`, SQLite provider) - see [Program.cs](ChangeTrackerBenchmark/Program.cs)
for the full, unedited code.

## The two query variants

```csharp
// Tracked (EF Core's default) - every materialized Quote is added to the
// context's identity map and change tracker.
var tracked = context.Quotes.ToList();

// AsNoTracking - no identity map, no change tracker entries. Each row is
// materialized straight into a Quote instance with nothing else attached.
var untracked = context.Quotes.AsNoTracking().ToList();
```

## Identity resolution

```csharp
using var identityContext = new QuotesDbContext(options);
var first  = identityContext.Quotes.First(q => q.Id == 1);
var second = identityContext.Quotes.First(q => q.Id == 1);
// ReferenceEquals(first, second) -> True
```

Real output:

```
=== Identity resolution ===
Tracked:      ReferenceEquals(first read, second read) = True
AsNoTracking: ReferenceEquals(first read, second read) = False
```

A tracked context keeps an identity map keyed by primary key - ask for `Id = 1` twice in the same
context and the second query returns the exact same object EF Core already has in memory, not a new
one freshly built from the second row returned by SQLite. `AsNoTracking()` has no identity map at all,
so two "identical" reads produce two independent objects - fine for read-only display code, a real
bug if calling code assumes `person1 == person2` implies the same reference.

## Tracked vs. not - proof from `ChangeTracker.Entries()`

```
=== ChangeTracker.Entries() after a full 10k-row read ===
After tracked read:      ChangeTracker.Entries().Count() = 10,000
After AsNoTracking read: ChangeTracker.Entries().Count() = 0
```

Every one of the 10,000 rows from the tracked query is sitting in the context's change tracker,
individually diffable and ready to have `SaveChanges()` called against it. The `AsNoTracking()` read
leaves the tracker completely empty - EF Core has already forgotten those objects exist the instant
the query returns them.

## Timing and allocation - the read-path win

Methodology: a fresh `DbContext` per iteration (so the tracked run's identity map/tracker never
carries state between iterations), 5 iterations per variant, averaged, `GC.Collect()` forced
immediately before each measured read so `GC.GetAllocatedBytesForCurrentThread()` isolates that read's
own allocations. One untimed warm-up query per variant beforehand pays for EF Core's model build and
SQLite connection open outside the measured numbers. Built and run in `Release`, not `Debug` - JIT
optimizations materially change both numbers.

Representative real run:

| | Mean time (ms) | Mean allocated (bytes) | Mean allocated (MB) |
|---|---:|---:|---:|
| Tracked | 51.43 | 12,178,080 | 11.61 |
| AsNoTracking | 16.48 | 6,135,014 | 5.85 |

```
Time ratio (tracked / no-tracking):      3.12x
Allocation ratio (tracked / no-tracking): 1.99x
```

Across several repeated runs, the **allocation ratio was rock-stable at 1.99x-2.00x** every time -
tracking a `Quote` costs roughly its own weight again in `EntityEntry`/snapshot bookkeeping per row.
The **time ratio varied more, from about 2.9x to 3.7x** across runs (this machine, ordinary process
scheduling noise, not a rigorous `BenchmarkDotNet` harness) - directionally consistent every single
run, but treat the exact multiplier as "roughly 3x," not a precise constant.

## One line on when you would NOT use `AsNoTracking()`

Don't use it for any query whose results you intend to mutate and persist with `SaveChanges()` -
an `AsNoTracking()` entity isn't in the change tracker, so changing its properties and calling
`SaveChanges()` does nothing at all (no exception, no update, just silent no-op persistence), unless
you explicitly re-attach it (`context.Update(entity)`) first.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-10/piece1

## Notes for mentor

Everything above is real, captured `dotnet run -c Release` output from
[ChangeTrackerBenchmark](ChangeTrackerBenchmark) - `dotnet run -c Release` from that folder reproduces
it (it re-seeds `benchmark.db` fresh on every run, so results are self-contained and don't depend on
prior state). Allocation is measured via `GC.GetAllocatedBytesForCurrentThread()` rather than a
profiler, since that's precise enough to show the ~2x difference clearly without adding a profiling
dependency; timing uses a plain `Stopwatch` over 5 iterations rather than `BenchmarkDotNet` (not
referenced anywhere else in this repo) to keep the exercise to a single runnable console app with no
extra tooling to install.

## What did I learn this session?

The allocation ratio (~2x, run after run) was far more stable than the timing ratio (2.9x-3.7x) - a
reminder that "how much memory did this allocate" is a deterministic property of what the code does,
while "how long did this take" is also a property of what else the OS/GC/scheduler happened to be
doing at that moment. When benchmarking anything without a tool like BenchmarkDotNet that controls for
that noise, allocation counts are the more trustworthy number to lead with.

## What would break this?

- This benchmark reads a `Quote` with only three scalar-ish columns (`Author`, `Text`, `CreatedAt`)
  and no navigation properties. An entity graph with related collections would make the tracked
  version's relative cost worse - the change tracker also has to build/maintain the relationship
  fix-up between parent and child entries, which this flat table never exercises.
- `AsNoTracking()`'s 0 `ChangeTracker.Entries()` result assumes nothing else in the same `DbContext`
  attached anything first. Call a tracked query and an `AsNoTracking()` query against the *same*
  context, and the tracked query's rows stay in the tracker - `AsNoTracking()` only means "don't track
  *this* query's results," not "clear the tracker."
- Forcing `GC.Collect()` before each measured read makes the allocation number attributable to that
  read alone, but it also means this benchmark doesn't show what steady-state allocation pressure
  (and GC pause frequency) looks like under a real, continuously-running workload that never gets a
  clean slate between requests.
