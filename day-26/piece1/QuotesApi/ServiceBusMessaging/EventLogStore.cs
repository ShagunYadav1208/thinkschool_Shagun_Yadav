using System.Collections.Concurrent;

namespace QuotesApi.ServiceBusMessaging;

public interface IEventLogStore<T>
{
    void Add(T entry);
    IReadOnlyCollection<T> GetAll();
}

/// <summary>Process-lifetime, thread-safe append log. One closed generic instance per consumer (see InfrastructureExtensions) - AuditLogEntry and NotificationEntry each get their own independent store.</summary>
public class EventLogStore<T> : IEventLogStore<T>
{
    private readonly ConcurrentQueue<T> _entries = new();

    public void Add(T entry) => _entries.Enqueue(entry);

    public IReadOnlyCollection<T> GetAll() => _entries.ToArray();
}
