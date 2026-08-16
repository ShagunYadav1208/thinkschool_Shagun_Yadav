namespace Quotes.Domain;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
