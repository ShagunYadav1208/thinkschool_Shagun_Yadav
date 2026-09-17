using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Payment.Application.Abstractions;
using ParkFlow.Modules.Payment.Application.IntegrationEvents;

namespace ParkFlow.Modules.Payment.Application.Payments;

using Domain = ParkFlow.Modules.Payment.Domain.Payment;

/// <summary>
/// Real wiring (see README, Flow 3) is a message broker consumer calling
/// <see cref="ChargeForReservationAsync"/> when a ReservationCompleted integration event arrives —
/// this piece exposes it directly for scaffolding purposes instead of building the consumer.
/// No real payment gateway integration; the charge always "succeeds" for now.
/// </summary>
public sealed class PaymentApplicationService(
    IPaymentRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventPublisher integrationEventPublisher)
{
    public async Task<Result<Guid>> ChargeForReservationAsync(Guid reservationId, decimal amount, CancellationToken cancellationToken = default)
    {
        var payment = Domain.CreateFor(reservationId, amount);
        payment.MarkCompleted();

        repository.Add(payment);

        await integrationEventPublisher.PublishAsync(
            new PaymentCompletedIntegrationEvent(payment.Id, reservationId, amount), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        payment.ClearDomainEvents();

        return Result.Success(payment.Id);
    }

    public async Task<Result> RefundAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await repository.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null)
        {
            return Result.Failure("Payment not found.");
        }

        payment.Refund();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
