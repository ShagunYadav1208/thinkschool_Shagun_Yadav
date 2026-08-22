using CqrsLiteApi.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CqrsLiteApi.Read;

// The READ side: a query and a read model shaped for exactly one screen -
// an author's quote feed. No Author/Quote domain entities cross this
// boundary; the DTO is already flat, already has the display string the
// screen wants, and is built with one AsNoTracking projection query, not
// by loading entities and mapping them afterward.
public record GetAuthorQuoteFeedQuery(int AuthorId) : IRequest<AuthorQuoteFeedDto?>;

public record AuthorQuoteFeedDto(
    int AuthorId,
    string AuthorName,
    int TotalQuotes,
    IReadOnlyList<QuoteFeedItemDto> Quotes);

public record QuoteFeedItemDto(
    int QuoteId,
    string Text,
    string PostedAgoDisplay);

public class GetAuthorQuoteFeedQueryHandler(AppDbContext db)
    : IRequestHandler<GetAuthorQuoteFeedQuery, AuthorQuoteFeedDto?>
{
    public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedQuery request, CancellationToken cancellationToken)
    {
        var author = await db.Authors.AsNoTracking()
            .Where(a => a.AuthorId == request.AuthorId)
            .Select(a => new
            {
                a.AuthorId,
                a.Name,
                TotalQuotes = a.Quotes.Count,
                // Newest first. Ordering by QuoteId (an ever-increasing identity
                // column) rather than CreatedAt directly, because the SQLite
                // provider can't translate ORDER BY on a DateTimeOffset column -
                // and insertion order already matches creation order here.
                Quotes = a.Quotes
                    .OrderByDescending(q => q.QuoteId)
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

    private static string FormatPostedAgo(TimeSpan age) => age switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)age.TotalMinutes}m ago",
        { TotalDays: < 1 } => $"{(int)age.TotalHours}h ago",
        _ => $"{(int)age.TotalDays}d ago"
    };
}
