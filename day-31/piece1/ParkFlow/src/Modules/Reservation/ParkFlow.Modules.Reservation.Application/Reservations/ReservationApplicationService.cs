using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Reservation.Application.Abstractions;
using ParkFlow.Modules.Reservation.Application.IntegrationEvents;
using ParkFlow.Modules.Reservation.Domain.DomainEvents;

namespace ParkFlow.Modules.Reservation.Application.Reservations;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

/// <summary>
/// Orchestrates the Reservation use cases (Flow 1 / Flow 4 in the README). Deliberately thin: all
/// the actual business rules live on the aggregate itself, this class just loads it, calls the
/// right method, and persists the result through the abstractions above — no EF Core, no HTTP, no
/// broker code in here.
/// </summary>
public sealed class ReservationApplicationService(
    IReservationRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventPublisher integrationEventPublisher)
{
    /// <summary>
    /// Rule 7: the same idempotency key must never create two reservations, so a retried "create"
    /// request (e.g. after a client timeout) returns the reservation created the first time instead
    /// of a duplicate. Rule 1 (no overlapping active reservations for the spot) is checked here too,
    /// though closing the race for real still needs a unique database constraint — see the README's
    /// "Double-booking prevention" note.
    /// </summary>
    public async Task<Result<Guid>> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(existing.Id);
        }

        var overlaps = await repository.HasOverlappingActiveReservationAsync(
            request.ParkingSpotId, request.StartTime, request.EndTime, cancellationToken);
        if (overlaps)
        {
            return Result.Failure<Guid>("The parking spot already has an active reservation for that time window.");
        }

        var reservation = Domain.Create(
            request.UserId, request.VehicleId, request.ParkingSpotId,
            request.StartTime, request.EndTime, request.Price, request.IdempotencyKey);

        repository.Add(reservation);

        foreach (var domainEvent in reservation.DomainEvents.OfType<ReservationCreatedDomainEvent>())
        {
            await integrationEventPublisher.PublishAsync(
                new ReservationCreatedIntegrationEvent(
                    domainEvent.ReservationId, domainEvent.UserId, domainEvent.VehicleId,
                    domainEvent.ParkingSpotId, domainEvent.StartTime, domainEvent.EndTime, domainEvent.Price),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        reservation.ClearDomainEvents();

        return Result.Success(reservation.Id);
    }

    /// <summary>
    /// Rule/ADR-001 (day-28/piece1): the ownership guard runs before touching the aggregate, so a
    /// non-owner gets Forbidden regardless of the reservation's current state — it must not be
    /// possible to learn anything about a reservation's status by probing it as the wrong user.
    /// </summary>
    public async Task<Result> CancelAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var reservation = await repository.GetByIdAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            return Result.NotFound("Reservation not found.");
        }

        if (reservation.UserId != currentUserId)
        {
            return Result.Forbidden("You do not own this reservation.");
        }

        reservation.Cancel();

        await integrationEventPublisher.PublishAsync(
            new ReservationCancelledIntegrationEvent(reservation.Id, reservation.ParkingSpotId),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        reservation.ClearDomainEvents();

        return Result.Success();
    }

    public async Task<Result> CheckInAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var reservation = await repository.GetByIdAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            return Result.NotFound("Reservation not found.");
        }

        if (reservation.UserId != currentUserId)
        {
            return Result.Forbidden("You do not own this reservation.");
        }

        reservation.CheckIn();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Flow 3: vehicle exit triggers Complete(), which is what downstream payment/spot-release
    /// react to.
    /// </summary>
    public async Task<Result> CompleteAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var reservation = await repository.GetByIdAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            return Result.NotFound("Reservation not found.");
        }

        if (reservation.UserId != currentUserId)
        {
            return Result.Forbidden("You do not own this reservation.");
        }

        reservation.Complete();

        await integrationEventPublisher.PublishAsync(
            new ReservationCompletedIntegrationEvent(reservation.Id, reservation.ParkingSpotId, DateTimeOffset.UtcNow),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        reservation.ClearDomainEvents();

        return Result.Success();
    }
}
