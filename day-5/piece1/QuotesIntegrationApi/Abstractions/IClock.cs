namespace QuotesIntegrationApi.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
