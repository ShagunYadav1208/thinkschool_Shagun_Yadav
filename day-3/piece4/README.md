# Day 3 - xUnit with Fluent Assertions

This piece sets up a small quote domain plus `Quotes.Tests.Unit` using:

- xUnit
- FluentAssertions
- NSubstitute

The tests follow AAA and use the naming pattern `Method_StateUnderTest_ExpectedBehavior`. There are no setup methods; each test arranges its own inputs. Parameterized branches use `[Theory]` with `[InlineData]`.

## Coverage

- Validators: every branch in `CreateQuoteRequestValidator`
- `Quote.Create`: success plus every failure mode
- Refresh-token reuse detection and refresh-family revocation
- `IClock`-using services with `FakeClock`

## Run

```bash
dotnet test
```

## Three sample tests

```csharp
[Fact]
public void Create_ValidInput_ReturnsTrimmedQuote()
{
    var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    var quote = Quote.Create(" Shagun ", " Testing gives confidence. ", createdAt, [" API ", "api", "Auth"]);

    quote.Author.Should().Be("Shagun");
    quote.Text.Should().Be("Testing gives confidence.");
    quote.Tags.Should().Equal("api", "auth");
}

[Fact]
public void Refresh_ReusedRotatedToken_RevokesReplacementToken()
{
    var clock = new FakeClock(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
    var reuseNotifier = Substitute.For<IRefreshReuseNotifier>();
    var service = new RefreshTokenService(clock, reuseNotifier);
    var original = service.Issue("user-123");
    var rotated = service.Refresh(original.RefreshToken).Tokens!;
    service.Refresh(original.RefreshToken);

    var result = service.Refresh(rotated.RefreshToken);

    result.IsSuccess.Should().BeFalse();
    result.ReuseDetected.Should().BeFalse();
}

[Fact]
public void IsExpired_AgeBelowTtl_ReturnsFalse()
{
    var createdAt = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
    var clock = new FakeClock(createdAt.AddMinutes(59));
    var service = new QuoteExpiryService(clock);
    var quote = Quote.Create("Shagun", "Testing gives confidence.", createdAt);

    var isExpired = service.IsExpired(quote, TimeSpan.FromHours(1));

    isExpired.Should().BeFalse();
}
```

## Test run output

```text
dotnet test

Test run for Quotes.Tests.Unit.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36, Duration: 86 ms - Quotes.Tests.Unit.dll (net10.0)
```

## GitHub link

https://github.com/ShagunYadav1208/thinkschool_Shagun_Yadav/tree/day3-pr/day-3/piece4

## Notes for mentor

The unit tests avoid hidden setup so each test shows the exact inputs, action, and assertion.

## What did I learn this session?

The useful click was that clean unit tests make domain rules visible: the test name says the rule, and FluentAssertions makes the expected behavior read naturally.

## What would break this?

Changing validation limits without updating the tests would fail fast, which is good. The refresh-token service is in-memory for this exercise; a real database implementation would need concurrency safeguards around token rotation.
