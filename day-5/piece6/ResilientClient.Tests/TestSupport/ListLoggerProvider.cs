using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ResilientClient.Tests.TestSupport;

public sealed record CapturedLogEntry(LogLevel Level, string Category, string Message);

/// <summary>
/// Captures every log entry written during a test so retry/circuit-breaker logging can be
/// asserted on directly, instead of only inferring behavior from HTTP responses.
/// </summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class ListLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLogEntry(logLevel, category, formatter(state, exception)));
        }
    }
}
