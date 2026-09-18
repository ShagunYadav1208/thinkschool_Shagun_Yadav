using Microsoft.EntityFrameworkCore;
using ParkFlow.Modules.Payment.Application.Abstractions;

namespace ParkFlow.Modules.Payment.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Payment.Domain.Payment;

public sealed class PaymentRepository(PaymentDbContext dbContext) : IPaymentRepository
{
    public Task<Domain?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Payments.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Domain?> GetByReservationIdAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        dbContext.Payments.SingleOrDefaultAsync(p => p.ReservationId == reservationId, cancellationToken);

    public void Add(Domain payment) => dbContext.Payments.Add(payment);
}
