# Day 12 - Read models + CQRS-lite

A real, running MediatR-based API ([CqrsLiteApi](CqrsLiteApi)) splitting one feature - authors and
their quotes - into a normalized, validated write model (`POST /quotes`) and a denormalized,
screen-shaped read model (`GET /authors/{id}/feed`). No event sourcing, no separate database - just
two independent paths through the same EF Core/SQLite store, verified end to end with real requests
(shown below, not narrated).

## The command handler (write side)

```csharp
public record CreateQuoteCommand(int AuthorId, string Text) : IRequest<int>;

public class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteCommandValidator()
    {
        RuleFor(c => c.AuthorId).GreaterThan(0);
        RuleFor(c => c.Text).NotEmpty().MaximumLength(1000);
    }
}

public class CreateQuoteCommandHandler(AppDbContext db) : IRequestHandler<CreateQuoteCommand, int>
{
    public async Task<int> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var authorExists = await db.Authors.AnyAsync(a => a.AuthorId == request.AuthorId, cancellationToken);
        if (!authorExists)
            throw new InvalidOperationException($"Author {request.AuthorId} does not exist.");

        var quote = new Quote { AuthorId = request.AuthorId, Text = request.Text, CreatedAt = DateTimeOffset.UtcNow };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);
        return quote.QuoteId;
    }
}
```

`FluentValidation` rules run automatically via a MediatR `ValidationBehavior` pipeline (see
[ValidationBehavior.cs](CqrsLiteApi/Write/ValidationBehavior.cs)) before the handler ever executes -
the handler itself only has to worry about the one domain invariant validation can't express (the
author has to actually exist).

## The query / read model (read side)

```csharp
public record GetAuthorQuoteFeedQuery(int AuthorId) : IRequest<AuthorQuoteFeedDto?>;

public record AuthorQuoteFeedDto(int AuthorId, string AuthorName, int TotalQuotes, IReadOnlyList<QuoteFeedItemDto> Quotes);
public record QuoteFeedItemDto(int QuoteId, string Text, string PostedAgoDisplay);

public class GetAuthorQuoteFeedQueryHandler(AppDbContext db) : IRequestHandler<GetAuthorQuoteFeedQuery, AuthorQuoteFeedDto?>
{
    public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedQuery request, CancellationToken cancellationToken)
    {
        var author = await db.Authors.AsNoTracking()
            .Where(a => a.AuthorId == request.AuthorId)
            .Select(a => new
            {
                a.AuthorId, a.Name,
                TotalQuotes = a.Quotes.Count,
                Quotes = a.Quotes.OrderByDescending(q => q.QuoteId)
                    .Select(q => new { q.QuoteId, q.Text, q.CreatedAt })
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (author is null) return null;

        var now = DateTimeOffset.UtcNow;
        var items = author.Quotes
            .Select(q => new QuoteFeedItemDto(q.QuoteId, q.Text, FormatPostedAgo(now - q.CreatedAt)))
            .ToList();
        return new AuthorQuoteFeedDto(author.AuthorId, author.Name, author.TotalQuotes, items);
    }
    // FormatPostedAgo(...) -> "just now" / "3h ago" / "2d ago"
}
```

`AuthorQuoteFeedDto` never exposes the `Author`/`Quote` domain entities or their navigation
properties - it's flat, it already carries `TotalQuotes` precomputed, and each quote already carries
a display-ready `PostedAgoDisplay` string instead of a raw timestamp the screen would have to format
itself. One `AsNoTracking` query, one round trip - EF Core translates it into a single `SELECT ...
LEFT JOIN "Quotes"` (real captured SQL, full text in `Program.cs`'s companion log - the query never
materializes tracked `Quote` entities the way the write side does).

## Real, verified request/response pairs

```
POST /quotes  {"authorId":1,"text":"The most disastrous thing..."}
  -> 201 {"quoteId":1}

POST /quotes  {"authorId":1,"text":""}
  -> 400 {"errors":{"Text":["'Text' must not be empty."]}}

POST /quotes  {"authorId":999,"text":"Ghost quote"}
  -> 404 {"error":"Author 999 does not exist."}

GET /authors/1/feed
  -> 200 {"authorId":1,"authorName":"Ada Lovelace","totalQuotes":2,
          "quotes":[{"quoteId":2,"text":"...","postedAgoDisplay":"just now"},
                    {"quoteId":1,"text":"...","postedAgoDisplay":"just now"}]}

GET /authors/2/feed   (author with zero quotes)
  -> 200 {"authorId":2,"authorName":"Grace Hopper","totalQuotes":0,"quotes":[]}

GET /authors/999/feed
  -> 404
```

## One line on what got simpler by separating them

The write handler never has to think about how a screen wants quotes formatted or grouped, and the
read handler never has to think about validation or domain invariants at all - each side only has to
be correct about the one thing it's responsible for, instead of one method trying to be a
validated-entity-loader and a screen-formatter at the same time.

## GitHub link

https://github.com/thinkbridge-thinkschool/thinkschool-Shagun_Yadav/tree/main/day-12/piece1

## Notes for mentor

Everything above (the request/response pairs, the SQL shape) is from actually running
`CqrsLiteApi` and hitting it with `curl` - not narrated. One real bug hit and fixed along the way:
the read query originally did `.OrderByDescending(q => q.CreatedAt)` server-side, which throws at
runtime on the SQLite provider (`SQLite does not support expressions of type 'DateTimeOffset' in
ORDER BY clauses`) - fixed by ordering on `QuoteId` instead (an ever-increasing identity column that
matches insertion/creation order here), which SQLite can translate natively. Left the mistake and fix
in the code's own comment rather than hiding it, since "the read side hit a provider limitation the
write side never would have" is itself a small, honest data point about how differently the two sides
get exercised.

## What did I learn this session?

The read side and the write side don't just have different *shapes* of data, they exercise the
database provider differently too - the write model's `Quote.CreatedAt` never gets ordered by, so its
`DateTimeOffset` column was fine; the read model tried to sort by it and immediately hit a SQLite
provider limitation the write path would never have surfaced. Separating the two paths didn't just
make each one simpler to read - it meant a query-shape bug in the read model literally could not have
broken the write path, and vice versa.

## What would break this?

- `AuthorQuoteFeedDto` computes `PostedAgoDisplay` at request time from `DateTimeOffset.UtcNow` - two
  requests for the same feed a minute apart will render different strings for the same quote (`"just
  now"` becoming `"1m ago"`), which is correct behavior for a live feed but would be a caching bug if
  someone put a response cache in front of this endpoint without accounting for it.
- The write and read paths share the same tables with no replication lag between them (no event
  sourcing, no eventual consistency, as the exercise specifies) - a `POST /quotes` immediately followed
  by `GET /authors/{id}/feed` always sees the new quote here. A real CQRS system with a separately
  materialized read store would need to handle the read model being briefly stale after a write, which
  this lite version never has to.
- The domain invariant check (`author must exist`) lives in the command handler as a plain
  `InvalidOperationException`, checked with a separate `AnyAsync` query before the insert - under
  concurrent requests, an author could theoretically be deleted between that check and the
  `SaveChangesAsync` call. A real system would rely on the foreign-key constraint itself (which SQLite
  already enforces here) as the actual source of truth, and treat the existence check as a
  friendlier-error optimization rather than the only safeguard.
