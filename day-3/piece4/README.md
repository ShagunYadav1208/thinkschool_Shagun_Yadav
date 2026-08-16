# Day 3 - xUnit with Fluent Assertions

`Quotes.Domain` is a plain class library holding the domain logic accumulated across earlier days (rich `Quote` entity, `Collection` aggregate, refresh-token rotation, `IClock`-based services), with zero ASP.NET Core / EF Core dependency so it's fast and trivial to unit test in isolation. `Quotes.Tests.Unit` covers it with xUnit + FluentAssertions + NSubstitute.

FluentAssertions is pinned to `7.0.0`, not the latest — v8+ requires a paid commercial license for non-personal use; 7.x stays free.

## Coverage

One test class per production class, named `Method_StateUnderTest_ExpectedBehavior`:

- **`QuoteTests`** — the `Quote.Create` factory: success, every failure mode (null/empty/whitespace author or text, over-length author or text, both invalid at once), boundary values at exactly the min/max length, and `Delete` (success + already-deleted throws).
- **`CollectionTests`** — validators, every branch: name required, too short, too long, boundary lengths, duplicate quote rejection, max-capacity rejection, boundary at exactly max capacity, remove-nonexistent rejection.
- **`RefreshTokenServiceTests`** — the reuse-detection logic: unknown token, expired token, valid rotation, a token revoked via logout (not reuse), a replayed already-replaced token (reuse detected, whole family revoked), and a token whose user no longer exists.
- **`QuoteServiceTests`** — the `IClock`-using service: uses a fake, fixed clock to assert `CreatedAt` exactly, verifies the repository is called with trimmed values, verifies invalid input throws without ever touching the repository (via NSubstitute's `DidNotReceive()`), and covers `DeleteAsync`'s found/not-found paths.

No shared `SetUp`/constructor-based fixtures — every test arranges its own substitutes and inputs explicitly, even where that repeats a line or two across tests.

## Run

```bash
dotnet test
```

## Sample tests

```csharp
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("   ")]
public void Create_WithNullOrWhitespaceAuthor_ReturnsFailureWithAuthorError(string? author)
{
    var result = Quote.Create(author, "Valid text", DateTimeOffset.UtcNow);

    result.IsSuccess.Should().BeFalse();
    result.Errors.Should().ContainSingle(error => error.Field == "author");
}
```

```csharp
[Fact]
public void Refresh_WithReplacedToken_DetectsReuseAndRevokesEveryActiveTokenInFamily()
{
    var store = Substitute.For<IRefreshTokenStore>();
    var users = Substitute.For<IUserStore>();
    var tokens = Substitute.For<ITokenService>();
    var familyId = Guid.NewGuid();
    tokens.HashRefreshToken("stolen-token").Returns("stolen-hash");
    var replacedRecord = new RefreshTokenRecord
    {
        TokenHash = "stolen-hash",
        UserId = "user-1",
        FamilyId = familyId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        RevokedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        ReplacedByTokenHash = "some-newer-hash"
    };
    var stillActiveSibling = new RefreshTokenRecord
    {
        TokenHash = "some-newer-hash",
        UserId = "user-1",
        FamilyId = familyId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };
    store.FindByHash("stolen-hash").Returns(replacedRecord);
    store.ActiveFamilyTokens(familyId).Returns([stillActiveSibling]);
    var service = new RefreshTokenService(store, users, tokens);

    var result = service.Refresh("stolen-token");

    result.Tokens.Should().BeNull();
    result.ReuseDetected.Should().BeTrue();
    stillActiveSibling.RevokedAt.Should().NotBeNull();
}
```

```csharp
[Fact]
public async Task CreateAsync_WithValidRequest_UsesClockForCreatedAt()
{
    var repository = Substitute.For<IQuoteRepository>();
    repository.AddAsync(Arg.Any<Quote>(), Arg.Any<CancellationToken>())
        .Returns(callInfo => Task.FromResult(callInfo.Arg<Quote>()));
    var fixedTime = new DateTimeOffset(2026, 8, 11, 9, 30, 0, TimeSpan.Zero);
    var clock = new FakeClock(fixedTime);
    var service = new QuoteService(repository, clock);

    var created = await service.CreateAsync("Maya Angelou", "Nothing will work unless you do.", CancellationToken.None);

    created.CreatedAt.Should().Be(fixedTime);
}
```

## Test run output

```
Test run for Quotes.Tests.Unit.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Test Run Successful.
Total tests: 43
     Passed: 43
 Total time: 0.6946 Seconds
```

43 tests, all green — well past the 20+ minimum.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-3/piece4

## Notes for mentor

`Quotes.Domain` re-implements the domain shapes built in earlier days (`Quote` from day-2/piece4, `Collection` from day-1/piece6, refresh-token rotation from day-2/piece6 and day-3/piece3, `IClock` from day-2/piece1) as a single dependency-free library purely so this piece has something real and varied to unit test without needing a running host or a database.
