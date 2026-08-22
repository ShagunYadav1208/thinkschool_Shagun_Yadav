using DapperVsEfApi.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DapperVsEfApi.Read;

// The EF Core version of the author-quote-feed read - carried over unchanged
// from day-12/piece1. This is the baseline the Dapper version below is
// measured against.
public record GetAuthorQuoteFeedEfQuery(int AuthorId) : IRequest<AuthorQuoteFeedDto?>;

public class GetAuthorQuoteFeedEfQueryHandler(AppDbContext db)
    : IRequestHandler<GetAuthorQuoteFeedEfQuery, AuthorQuoteFeedDto?>
{
    public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedEfQuery request, CancellationToken cancellationToken)
    {
        var author = await db.Authors.AsNoTracking()
            .Where(a => a.AuthorId == request.AuthorId)
            .Select(a => new
            {
                a.AuthorId,
                a.Name,
                TotalQuotes = a.Quotes.Count,
                // Ordering by QuoteId, not CreatedAt - SQLite's EF Core provider
                // can't translate ORDER BY on a DateTimeOffset column, and
                // insertion order already matches creation order here.
                Quotes = a.Quotes
                    .OrderByDescending(q => q.QuoteId)
                    .Select(q => new { q.QuoteId, q.Text, q.CreatedAt })
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (author is null) return null;

        var now = DateTimeOffset.UtcNow;
        var items = author.Quotes
            .Select(q => new QuoteFeedItemDto(q.QuoteId, q.Text, PostedAgoFormatter.Format(now - q.CreatedAt)))
            .ToList();

        return new AuthorQuoteFeedDto(author.AuthorId, author.Name, author.TotalQuotes, items);
    }
}
