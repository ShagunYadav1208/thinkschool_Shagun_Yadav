namespace QuotesApi.ServiceBusMessaging;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public required string FullyQualifiedNamespace { get; init; }
    public required string TopicName { get; init; }
    public required string AuditLogSubscription { get; init; }
    public required string NotificationsSubscription { get; init; }
}
