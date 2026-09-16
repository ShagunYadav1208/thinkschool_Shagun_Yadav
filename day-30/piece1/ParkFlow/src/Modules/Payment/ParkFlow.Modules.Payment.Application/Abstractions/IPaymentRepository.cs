namespace ParkFlow.Modules.Payment.Application.Abstractions;

using Domain = ParkFlow.Modules.Payment.Domain.Payment;

public interface IPaymentRepository
{
    Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Domain?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default);

    void Add(Domain payment);
}
