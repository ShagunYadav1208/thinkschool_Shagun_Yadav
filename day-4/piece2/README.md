# Day 4 - Drive the Day 3 auth codebase to 80% coverage

This piece reuses `QuotesLockedApi` from Day 3 Piece 3 (the "lock down the API end-to-end" auth
codebase: dual JWT schemes, refresh-token rotation with reuse detection, and custom policy-based
authorization) unmodified in logic, plus one small extraction described below, and drives it from
86.3% to 94.9% line coverage / 89.1% branch coverage with tests that would actually catch a
regression — not tests written to move a number.

## Run

```bash
dotnet restore day-4/piece2
dotnet build day-4/piece2 --no-restore --configuration Release
dotnet test day-4/piece2 --no-build --configuration Release \
  --logger "trx;LogFileName=test-results.trx" \
  --collect:"XPlat Code Coverage" \
  --results-directory day-4/piece2/TestResults
```

42 tests, all passing.

## Coverage report

Cobertura summary (via `dotnet-reportgenerator-globaltool`, `-reporttypes:TextSummary`):

```
Line coverage: 94.9%   (299 of 315 coverable lines)
Branch coverage: 89.1% (66 of 74)
Method coverage: 100%  (55 of 55)

QuotesLockedApi                               94.9%
  Program                                     89.8%   <- see "the one gap" below
  QuotesLockedApi.AccessTokenResponse          100%
  QuotesLockedApi.CreateQuoteRequest           100%
  QuotesLockedApi.DeleteOwnQuoteHandler        100%
  QuotesLockedApi.EntraOptions                 100%
  QuotesLockedApi.InMemoryQuoteStore           100%
  QuotesLockedApi.InMemoryRefreshTokenStore    100%
  QuotesLockedApi.InMemoryUserStore            100%
  QuotesLockedApi.InternalTokenRequest         100%
  QuotesLockedApi.JwtOptions                   100%
  QuotesLockedApi.LoginRequest                 100%
  QuotesLockedApi.Quote                        100%
  QuotesLockedApi.RefreshRequest               100%
  QuotesLockedApi.RefreshResult                100%
  QuotesLockedApi.RefreshTokenRecord           100%
  QuotesLockedApi.RefreshTokenService          100%
  QuotesLockedApi.SmartBearerRouting           100%
  QuotesLockedApi.TokenResponse                100%
  QuotesLockedApi.TokenService                 100%
  QuotesLockedApi.UpdateQuoteRequest            100%
  QuotesLockedApi.User                         100%
```

Baseline before today's tests (same code, only Day 3's original 6 tests): **86.3% line / 64.8%
branch**. So the line-coverage number was already over 80% on day one — the real work here was
closing the branch-coverage gap (64.8% → 89.1%) with tests that exercise scenarios the original
suite never touched at all: `POST /quotes` had **zero** coverage, `PUT` on a missing quote had
never been hit, login/refresh/internal-token failure paths were untested, and a non-owner trying
to delete someone else's quote — the one scenario `DeleteOwnQuoteRequirement` exists to stop — had
never been exercised either.

## The architecture fix: `SmartBearerRouting`

The dual-JWT scheme selector (`GetBearerToken`, `ReadIssuer`, `IsEntraIssuer`,
`GetAllowedEntraAudiences`) used to live as `static` local functions at the bottom of the
top-level-statements `Program.cs`. That makes them literally impossible to unit test in isolation —
the only way to exercise them was a full HTTP round trip through `WebApplicationFactory`, and for
the Entra path that meant either a live Microsoft Entra tenant or a fake test. That's the "hard to
test code is usually wrongly-coupled code" smell the exercise warns about: the coupling wasn't to
Entra itself, it was to `Program.cs`'s top-level-statement scope.

The fix: extracted all four functions into `QuotesLockedApi/Authentication/SmartBearerRouting.cs`,
a plain `public static class` with no ASP.NET Core dependency beyond `JwtSecurityTokenHandler`.
`Program.cs` now just calls `SmartBearerRouting.SelectScheme(...)` and
`SmartBearerRouting.GetAllowedEntraAudiences(...)`. `SmartBearerRoutingTests.cs` covers every branch
directly — including feeding it a hand-built JWT with a `https://login.microsoftonline.com/...`
issuer to prove the Entra routing decision is correct — without ever needing a real Entra tenant or
a network call.

## The one remaining gap, and why it's not faked

`Program.cs` sits at 89.8%, and the only uncovered lines are the `.AddJwtBearer(EntraJwtScheme, ...)`
configuration block (`Authority`, `TokenValidationParameters` for the Entra scheme). This is
deliberately left uncovered rather than faked, for a concrete reason discovered while doing this
exercise:

`AddJwtBearer(scheme, configureOptions)` doesn't run `configureOptions` at startup — it registers it
with the options system and only invokes it lazily, the first time something actually resolves
`JwtBearerOptions` for that specific scheme. Since every test token in this suite carries an
internal issuer, `SmartBearerRouting.SelectScheme` always routes to `InternalJwtScheme`, and the
Entra scheme's options are never resolved, so that configuration block never runs — not even once,
in any test, including ones that only touch unrelated endpoints.

Making that block "covered" without a real Entra tenant would mean one of:
1. A test that manufactures a JWT with a Microsoft-looking issuer and sends it through — but the
   Entra `JwtBearerHandler` would then try to fetch OIDC discovery metadata from a live Microsoft
   endpoint (`options.Authority`) to validate it, which is a real network call with no business
   being in a test suite (slow, flaky, and blocked in most CI sandboxes anyway).
2. Standing up a fake OIDC discovery/JWKS endpoint inside the test host and pointing `Authority` at
   it — a legitimate fix, but a bigger architecture investment (an injectable token-validation
   abstraction) than this session's scope.

So this is the "if you can't get to 80% without writing fake tests, find the architecture problem"
case working as intended: the number was already achievable elsewhere, and the honest move for this
one spot was to explain the gap instead of covering it with a test that doesn't actually verify
anything real.

## GitHub link

https://github.com/thinkbridge-thinkschool/your-repo/tree/main/thinkschool_Shagun_Yadav/day-4/piece2

## Notes for mentor

Reused Day 3 Piece 3's `QuotesLockedApi` and `AuthIntegrationTests.cs` as the starting point,
copied unmodified, then: (1) extracted `SmartBearerRouting` out of `Program.cs`'s local functions,
(2) added `coverlet.collector` to the test project so `--collect:"XPlat Code Coverage"` actually
produces a Cobertura file, (3) added 36 new tests across four new files
(`SmartBearerRoutingTests.cs`, `RefreshTokenServiceTests.cs`, `DeleteOwnQuoteHandlerTests.cs`,
`JwtOptionsTests.cs`) plus extended `AuthIntegrationTests.cs` and added `QuotesEndpointTests.cs`.

## What did I learn this session?

`AddJwtBearer`'s configuration delegate is lazy, not eager — it only runs when that scheme is
actually selected and its options resolved. In a multi-scheme setup like this one, that means an
entire scheme's configuration can go completely unexercised by a test suite without any test
failing or any coverage tool screaming loudly about it (until you actually read *which* lines are
uncovered) — a misconfigured `Audience` or a typo'd tenant ID in the Entra options would ship
silently. The fix isn't a test, it's recognizing the coupling and being explicit about the gap.

## What would break this?

If Microsoft Entra ever starts sending `scp` as a space-separated string claim instead of (or in
addition to) individually repeated `scope` claims, `can-edit-quotes`'s
`RequireClaim("scope", "quotes.write")` policy would silently reject legitimate Entra callers — and
nothing in this suite would catch it, since every test token here is internally issued. That's the
same root gap as the uncovered Entra block above: the Entra path is real production code with zero
test coverage today.
