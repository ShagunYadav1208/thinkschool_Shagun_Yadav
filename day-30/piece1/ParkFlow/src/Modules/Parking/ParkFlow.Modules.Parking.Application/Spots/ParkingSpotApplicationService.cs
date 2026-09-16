using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Parking.Application.Abstractions;
using ParkFlow.Modules.Parking.Application.Availability;
using ParkFlow.Modules.Parking.Application.IntegrationEvents;

namespace ParkFlow.Modules.Parking.Application.Spots;

/// <summary>
/// Reacts to the Reservation module's lifecycle. In this piece these methods are called directly
/// by the API for scaffolding purposes; the real wiring (see README's async flows) is a message
/// broker consumer in Infrastructure that calls <see cref="ReleaseAsync"/> whenever it receives
/// ReservationCancelled, ReservationExpired, ReservationCompleted, or a no-show — never a direct
/// call from the Reservation module's code, which would couple the two modules' domain models.
/// </summary>
public sealed class ParkingSpotApplicationService(
    IParkingSpotRepository spotRepository,
    IUnitOfWork unitOfWork,
    IParkingAvailabilityCache availabilityCache,
    IIntegrationEventPublisher integrationEventPublisher)
{
    public async Task<Result> ReserveAsync(Guid spotId, CancellationToken cancellationToken = default)
    {
        var spot = await spotRepository.GetByIdAsync(spotId, cancellationToken);
        if (spot is null)
        {
            return Result.Failure("Parking spot not found.");
        }

        spot.Reserve();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await availabilityCache.InvalidateAsync(spot.FacilityId, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> OccupyAsync(Guid spotId, CancellationToken cancellationToken = default)
    {
        var spot = await spotRepository.GetByIdAsync(spotId, cancellationToken);
        if (spot is null)
        {
            return Result.Failure("Parking spot not found.");
        }

        spot.Occupy();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Rule 8: releasing a spot is what makes it available for the next driver again.</summary>
    public async Task<Result> ReleaseAsync(Guid spotId, CancellationToken cancellationToken = default)
    {
        var spot = await spotRepository.GetByIdAsync(spotId, cancellationToken);
        if (spot is null)
        {
            return Result.Failure("Parking spot not found.");
        }

        spot.Release();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await availabilityCache.InvalidateAsync(spot.FacilityId, cancellationToken);

        await integrationEventPublisher.PublishAsync(
            new ParkingSpotReleasedIntegrationEvent(spot.Id, spot.FacilityId), cancellationToken);

        return Result.Success();
    }
}
