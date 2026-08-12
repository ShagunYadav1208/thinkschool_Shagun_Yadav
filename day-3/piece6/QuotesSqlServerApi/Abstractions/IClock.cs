namespace QuotesSqlServerApi.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
