using System.Globalization;
using Dapper;
using DapperVsEfApi.Data;
using MediatR;

namespace DapperVsEfApi.Read;

// The Dapper version of the exact same read - same result shape
// (AuthorQuoteFeedDto), same underlying SQLite file, same "does this author
// exist, and what are their quotes" question. The difference is entirely in
// HOW the data gets from the database into that DTO: raw SQL text and a
// thin, reflection-emit-based mapper, instead of LINQ translation, change
// tracking setup, and EF Core's query pipeline.
public record GetAuthorQuoteFeedDapperQuery(int AuthorId) : IRequest<AuthorQuoteFeedDto?>;

public class GetAuthorQuoteFeedDapperQueryHandler(SqliteConnectionFactory connectionFactory)
    : IRequestHandler<GetAuthorQuoteFeedDapperQuery, AuthorQuoteFeedDto?>
{
    private const string Sql = """
        SELECT AuthorId, Name,
               (SELECT COUNT(*) FROM Quotes q WHERE q.AuthorId = a.AuthorId) AS TotalQuotes
        FROM Authors a
        WHERE AuthorId = @AuthorId;

        SELECT QuoteId, Text, CreatedAt AS CreatedAtRaw
        FROM Quotes
        WHERE AuthorId = @AuthorId
        ORDER BY QuoteId DESC;
        """;

    public async Task<AuthorQuoteFeedDto?> Handle(GetAuthorQuoteFeedDapperQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        var command = new CommandDefinition(Sql, new { request.AuthorId }, cancellationToken: cancellationToken);
        using var multi = await connection.QueryMultipleAsync(command);

        var author = await multi.ReadSingleOrDefaultAsync<AuthorRow>();
        if (author is null) return null;

        var quoteRows = (await multi.ReadAsync<QuoteRow>()).ToList();

        var now = DateTimeOffset.UtcNow;
        var items = quoteRows
            .Select(q =>
            {
                // EF Core's SQLite provider has an internal value converter that
                // silently turns the TEXT column back into a DateTimeOffset on
                // the way out - raw ADO.NET/Dapper has no such converter, so the
                // column arrives as the plain ISO-8601 string EF Core wrote and
                // has to be parsed by hand here.
                var createdAt = DateTimeOffset.Parse(q.CreatedAtRaw, CultureInfo.InvariantCulture);
                return new QuoteFeedItemDto(q.QuoteId, q.Text, PostedAgoFormatter.Format(now - createdAt));
            })
            .ToList();

        return new AuthorQuoteFeedDto(author.AuthorId, author.Name, author.TotalQuotes, items);
    }

    // Plain classes with settable properties, not positional records: SQLite
    // returns INTEGER columns as Int64, and Dapper's constructor-matching for
    // records needs an EXACT parameter-type match (int vs long fails outright,
    // as it did here on the first run). Property-setter mapping coerces long
    // -> int automatically instead.
    private class AuthorRow
    {
        public int AuthorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalQuotes { get; set; }
    }

    private class QuoteRow
    {
        public int QuoteId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string CreatedAtRaw { get; set; } = string.Empty;
    }
}
