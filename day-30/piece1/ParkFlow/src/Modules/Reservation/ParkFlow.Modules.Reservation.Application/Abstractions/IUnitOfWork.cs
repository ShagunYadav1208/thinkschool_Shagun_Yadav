namespace ParkFlow.Modules.Reservation.Application.Abstractions;

/// <summary>
/// One database transaction, one outbox write, one commit — see Flow 1 in the README for why
/// those two things must land together.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
