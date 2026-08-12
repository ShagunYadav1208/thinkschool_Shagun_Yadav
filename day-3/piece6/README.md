# Day 3 - Real SQL Server in CI with Testcontainers

`QuotesSqlServerApi` is the same shape of Quotes API as the Day 3 Piece 5 SQLite version, but its EF
Core provider now targets real SQL Server (`UseSqlServer`, SQL-Server-flavored migrations, explicit
`nvarchar` column lengths on `Author`/`Text`). `Quotes.Tests.Integration.SqlServer` exercises it through
`WebApplicationFactory<Program>` against an actual SQL Server 2022 container started by
`Testcontainers.MsSql` — not SQLite, not the EF in-memory provider. That is the whole point: SQLite
happily stores a 2000-character string in an unbounded column and never enforces collation; real SQL
Server will truncate or reject it if the schema says otherwise, and only a real engine surfaces that.

## Container lifecycle: once per run, not once per test

Booting a SQL Server container takes a few seconds even when the image is cached, so unlike the SQLite
version (fresh in-memory database per test), this suite starts **one** container for the whole test run
and tears it down after the last test finishes. Isolation comes from each test seeding its own uniquely
named data (`UniqueAuthor(...)`, GUID-suffixed) and asserting only against the rows it created — never
from assuming the table is empty.

## Testcontainers fixture

```csharp
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SQL Server container collection";
}
```

`ICollectionFixture<SqlServerContainerFixture>` is what makes xUnit start the container once before any
test in the `[Collection(SqlServerCollection.Name)]` group runs, and dispose it once after the last one
finishes — exactly "spin up before any tests run; tear down after."

## WebApplicationFactory override using the Testcontainers connection string

```csharp
public sealed class QuotesSqlServerApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IClock>();

            services.AddDbContext<QuotesDbContext>(options => options.UseSqlServer(connectionString));
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        await db.Database.MigrateAsync();
    }
}
```

Each test class instance builds its own `QuotesSqlServerApiFactory` pointed at the one shared
container's connection string, so the in-process `TestServer`/`HttpClient` pair is still fresh per test
— only the underlying database is shared:

```csharp
[Collection(SqlServerCollection.Name)]
public sealed class QuotesEndpointTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlFixture;
    private QuotesSqlServerApiFactory _factory = null!;
    private HttpClient _client = null!;

    public QuotesEndpointTests(SqlServerContainerFixture sqlFixture) => _sqlFixture = sqlFixture;

    public async Task InitializeAsync()
    {
        _factory = new QuotesSqlServerApiFactory(_sqlFixture.ConnectionString);
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }
}
```

## GitHub Actions snippet

`ubuntu-latest` ships Docker pre-installed and running, so Testcontainers works with no extra setup —
only the image pull needs caching to avoid paying ~5 minutes on every run:

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - name: Cache Docker layers
        uses: actions/cache@v4
        with:
          path: /tmp/docker-cache
          key: mssql-2022-image-${{ runner.os }}
      - name: Restore cached SQL Server image
        run: |
          if [ -f /tmp/docker-cache/mssql-2022.tar ]; then
            docker load -i /tmp/docker-cache/mssql-2022.tar
          fi
      - run: dotnet test thinkschool_Shagun_Yadav/day-3/piece6
      - name: Save SQL Server image to cache
        if: always()
        run: |
          mkdir -p /tmp/docker-cache
          docker save mcr.microsoft.com/mssql/server:2022-latest -o /tmp/docker-cache/mssql-2022.tar
```

Full workflow: [.github/workflows/day3-piece6.yml](.github/workflows/day3-piece6.yml)

## Test run output

Real run, cold Docker image cache (first pull of `mcr.microsoft.com/mssql/server:2022-latest` took
~1m50s of the ~2m26s total; the exercise's own "~5 min on first run" estimate matches — this machine's
network made it faster, CI runners are typically slower):

```
$ dotnet test
Test run for Quotes.Tests.Integration.SqlServer.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.
[testcontainers.org] Connected to Docker: Server Version: 29.6.1, Operating System: Docker Desktop
[testcontainers.org] Docker image mcr.microsoft.com/mssql/server:2022-latest created
[testcontainers.org] Docker container c1f20c175143 created
[testcontainers.org] Wait for Docker container c1f20c175143 to complete readiness checks
[testcontainers.org] Docker container c1f20c175143 ready
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20260812101002_InitialCreate'.
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      CREATE TABLE [Quotes] (
          [Id] int NOT NULL IDENTITY,
          [Author] nvarchar(100) NOT NULL,
          [Text] nvarchar(1000) NOT NULL,
          [CreatedAt] datetimeoffset NOT NULL,
          CONSTRAINT [PK_Quotes] PRIMARY KEY ([Id])
      );
...
  Passed Quotes.Tests.Integration.SqlServer.QuotesEndpointTests.DeleteQuote_WithTokenWhenExists_ReturnsNoContentAndRemovesIt [196 ms]
  Passed Quotes.Tests.Integration.SqlServer.QuotesEndpointTests.CreateQuote_WithBlankText_ReturnsValidationProblem [14 ms]
  Passed Quotes.Tests.Integration.SqlServer.QuotesEndpointTests.Migrations_AreAppliedAtStartup_QuotesTableIsQueryableOnRealSqlServer [65 ms]
  Passed Quotes.Tests.Integration.SqlServer.QuotesEndpointTests.DeleteQuote_WithTokenWhenMissing_ReturnsNotFound [17 ms]

Test Run Successful.
Total tests: 13
     Passed: 13
 Total time: 2.4266 Minutes
```

The generated `CREATE TABLE` above is worth pointing at directly: `nvarchar(100)`/`nvarchar(1000)` are
SQL Server types with real enforced lengths, and `datetimeoffset` is SQL Server's own type — none of
that schema exists when the same model runs against SQLite in Piece 5.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece6

## Notes for mentor

`Program.cs`'s own startup code still calls `await db.Database.MigrateAsync();` before `app.Run()`, same
as every other piece — that's what actually creates the schema on the real SQL Server container the
first time any test in the collection boots a `TestServer`. The factory's `InitializeAsync` calls
`MigrateAsync` a second time; EF Core migrations are idempotent so this is just a safety net, not a
requirement.

`dotnet ef migrations add` couldn't reach a live SQL Server from this machine at design time (no local
SQL Server instance running), so `Data/QuotesDbContextFactory.cs` (an `IDesignTimeDbContextFactory`)
gives the EF tooling a `DbContextOptions` built from a literal placeholder connection string instead of
running the full app startup path — the standard fix when your `Program.cs` needs a real database
connection to boot but migration authoring shouldn't require one.

## What did I learn this session?

The part that clicked: a Testcontainers-backed suite has two separable lifetimes, and conflating them is
the classic mistake. The **container** is expensive and belongs to the whole run (`ICollectionFixture`
before any test, disposed after the last one). The **HTTP pipeline under test** is cheap and belongs to
each test (`IAsyncLifetime` on the test class itself). Isolation moves from "fresh database" (Piece 5,
SQLite) to "uniquely named rows, asserted narrowly" (this piece) the moment the database becomes a
shared, expensive resource.

## What would break this?

- Running this locally without Docker (or with Docker Desktop not started) fails at
  `MsSqlContainer.StartAsync()` with a connection error to the Docker daemon — there is no fallback path.
- A test that assumes the `Quotes` table is empty (the SQLite-piece pattern) would be flaky here, since
  every test in the collection writes into the same real database; every test in this suite instead
  seeds a GUID-suffixed author and only asserts against rows it created or an id it just got back.
- SQL Server's default collation is case-insensitive, accent-insensitive comparison for `nvarchar` —
  a test asserting `Author == "ada lovelace"` matches a row seeded as `"Ada Lovelace"` on real SQL
  Server in a way SQLite's default binary comparison would not. This suite never does an
  equality-by-text lookup for that reason; it always looks up by the numeric `Id` the API just returned.
