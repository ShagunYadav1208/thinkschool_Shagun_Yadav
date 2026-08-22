namespace DapperVsEfApi.Read;

// The read model both the EF and Dapper query handlers build - identical
// shape either way, so the comparison below is purely about how the data
// gets fetched, not about two different DTOs.
public record AuthorQuoteFeedDto(
    int AuthorId,
    string AuthorName,
    int TotalQuotes,
    IReadOnlyList<QuoteFeedItemDto> Quotes);

public record QuoteFeedItemDto(
    int QuoteId,
    string Text,
    string PostedAgoDisplay);

public static class PostedAgoFormatter
{
    public static string Format(TimeSpan age) => age switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)age.TotalMinutes}m ago",
        { TotalDays: < 1 } => $"{(int)age.TotalHours}h ago",
        _ => $"{(int)age.TotalDays}d ago"
    };
}
