# Day 3 - Integration tests with WebApplicationFactory

`QuotesIntegrationApi` is a minimal Quotes API (EF Core + SQLite, JWT bearer auth, `ProblemDetails`
validation) built specifically to be exercised end-to-end by `Quotes.Tests.Integration`, which boots
the real app in-memory via `WebApplicationFactory<Program>`.

- Real pipeline: routing, model binding, validation, authentication, authorization, EF Core — nothing
  mocked except the database engine (real SQLite, just in-memory) and the clock.
- `IClock` is swapped for a `FakeClock` so `Quote.CreatedAt` is deterministic and assertable.
- `QuotesDbContext` is swapped from the file-based SQLite connection string to an open in-memory
  `SqliteConnection`, then migrated with the project's real `InitialCreate` migration — so the test
  proves migrations actually apply, not just that some schema exists.
- Every test gets its own `QuotesApiFactory` and its own SQLite connection (created in
  `IAsyncLifetime.InitializeAsync`, disposed in `DisposeAsync`), so there is zero shared state between
  tests — no `IClassFixture`, because that would share one instance (and one database) across a whole
  test class.

## WebApplicationFactory subclass

```csharp
public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IClock>();

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<QuotesDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<IClock>(Clock);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection?.Dispose();
    }
}
```

Each test class implements `IAsyncLifetime` itself (not `IClassFixture<QuotesApiFactory>`) so a brand
new factory — and brand new in-memory database — is created and migrated before every single test
method, and torn down after it:

```csharp
public sealed class QuotesEndpointTests : IAsyncLifetime
{
    private QuotesApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new QuotesApiFactory();
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

## Two integration tests

Happy path — authenticated create, and proof the fake clock was actually used:

```csharp
[Fact]
public async Task CreateQuote_WithValidTokenAndBody_ReturnsCreatedAndStampsFakeClock()
{
    var token = await IssueTokenAsync("writer-1");

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
    {
        Content = JsonContent.Create(new { author = "Ada Lovelace", text = "That brain of mine is something more than merely mortal." })
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.NotNull(response.Headers.Location);

    var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
    Assert.NotNull(created);
    Assert.Equal("Ada Lovelace", created!.Author);
    Assert.Equal(_factory.Clock.UtcNow, created.CreatedAt);
}
```

Error path — validation failure returns a real `ProblemDetails` body, not just a status code:

```csharp
[Fact]
public async Task CreateQuote_WithBlankText_ReturnsValidationProblem()
{
    var token = await IssueTokenAsync("writer-1");

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
    {
        Content = JsonContent.Create(new { author = "Ada Lovelace", text = "   " })
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await _client.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.NotNull(problem);
    Assert.True(problem!.Errors.ContainsKey("text"));
}
```

## All 13 tests

`QuotesEndpointTests.cs` covers every endpoint, success and failure paths, auth required with and
without a token, `ProblemDetails` validation, and an explicit migrations-applied check:

- `CreateQuote_WithValidTokenAndBody_ReturnsCreatedAndStampsFakeClock`
- `CreateQuote_WithBlankText_ReturnsValidationProblem`
- `GetQuotes_WhenDatabaseIsEmpty_ReturnsEmptyList`
- `GetQuotes_WithPageBelowOne_ReturnsValidationProblem`
- `GetQuotes_WithSizeAboveMaximum_ReturnsValidationProblem`
- `GetQuoteById_WhenMissing_ReturnsNotFound`
- `GetQuoteById_WhenExists_ReturnsQuote`
- `CreateQuote_WithoutToken_ReturnsUnauthorized`
- `CreateQuote_WithAuthorTooLong_ReturnsValidationProblem`
- `DeleteQuote_WithoutToken_ReturnsUnauthorized`
- `DeleteQuote_WithTokenWhenExists_ReturnsNoContentAndRemovesIt`
- `DeleteQuote_WithTokenWhenMissing_ReturnsNotFound`
- `Migrations_AreAppliedAtStartup_QuotesTableIsQueryable`

## Test run output

```
$ dotnet test
Determining projects to restore...
Restored QuotesIntegrationApi.csproj
Restored Quotes.Tests.Integration.csproj
  QuotesIntegrationApi -> QuotesIntegrationApi\bin\Debug\net10.0\QuotesIntegrationApi.dll
  Quotes.Tests.Integration -> Quotes.Tests.Integration\bin\Debug\net10.0\Quotes.Tests.Integration.dll
Test run for Quotes.Tests.Integration\bin\Debug\net10.0\Quotes.Tests.Integration.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    13, Skipped:     0, Total:    13, Duration: 4 s - Quotes.Tests.Integration.dll (net10.0)
```

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece5

## Notes for mentor

`Program.cs` already migrates the database at startup (`await db.Database.MigrateAsync();`), and that
code runs unchanged inside the test host too — `WebApplicationFactory` executes everything up to
`app.Run()`. The factory's own `InitializeAsync` calls `MigrateAsync` a second time as a deliberate
belt-and-suspenders check; EF Core migrations are idempotent, so this just confirms the schema is
guaranteed ready before the first request, independent of any future change to startup ordering.

## What did I learn this session?

The part that clicked: isolation is a lifetime problem, not a data-cleanup problem. Wiping rows between
tests still leaves you sharing one `SqliteConnection`/schema/identity sequence across the whole class.
Giving each test its own factory (and therefore its own connection) via `IAsyncLifetime` instead of
`IClassFixture` is what actually makes tests independent — and it costs almost nothing since spinning
up an in-memory SQLite database is fast.

## What would break this?

Running two tests against the *same* `QuotesApiFactory` (e.g. switching back to `IClassFixture`) would
let `Id` values and row counts leak between tests and make `GetQuotes_WhenDatabaseIsEmpty_ReturnsEmptyList`
fail depending on test execution order. Forgetting to keep the `SqliteConnection` open for the factory's
lifetime would also break everything — SQLite deletes a `:memory:` database the moment its last
connection closes, so a per-DbContext connection string instead of a shared open connection would give
each `QuotesDbContext` its own empty, unmigrated database.
