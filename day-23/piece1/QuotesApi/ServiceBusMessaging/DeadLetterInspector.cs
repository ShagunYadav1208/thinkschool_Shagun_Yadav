using System.Text;
using Azure.Messaging.ServiceBus;

namespace QuotesApi.ServiceBusMessaging;

public record DeadLetterMessageView(
    string MessageId,
    string Body,
    int DeliveryCount,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    DateTimeOffset EnqueuedTime);

public interface IDeadLetterInspector
{
    Task<IReadOnlyList<DeadLetterMessageView>> PeekNotificationsDeadLetterAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Peeks (does not lock/consume) the notifications subscription's dead-letter sub-queue - the
/// proof that a poison message actually landed there, independent of anything the app itself
/// logged while processing it.
/// </summary>
public class DeadLetterInspector(ServiceBusClient client, ServiceBusOptions options) : IDeadLetterInspector
{
    public async Task<IReadOnlyList<DeadLetterMessageView>> PeekNotificationsDeadLetterAsync(CancellationToken cancellationToken)
    {
        await using var receiver = client.CreateReceiver(
            options.TopicName,
            options.NotificationsSubscription,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var messages = await receiver.PeekMessagesAsync(maxMessages: 50, cancellationToken: cancellationToken);

        return messages
            .Select(m => new DeadLetterMessageView(
                m.MessageId,
                Encoding.UTF8.GetString(m.Body),
                m.DeliveryCount,
                m.DeadLetterReason,
                m.DeadLetterErrorDescription,
                m.EnqueuedTime))
            .ToList();
    }
}
