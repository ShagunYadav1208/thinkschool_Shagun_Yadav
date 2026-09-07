namespace QuotesApi.ServiceBusMessaging;

public interface IQuoteEventPublisher
{
    Task PublishAsync(QuoteCreatedEvent quoteEvent, CancellationToken cancellationToken);
}
