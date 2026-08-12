using QuotesSqlServerApi.Abstractions;

namespace Quotes.Tests.Integration.SqlServer;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
