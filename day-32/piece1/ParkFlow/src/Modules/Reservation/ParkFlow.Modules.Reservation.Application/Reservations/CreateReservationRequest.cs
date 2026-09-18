using System.ComponentModel.DataAnnotations;
using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Reservation.Application.Reservations;

public sealed record CreateReservationRequest(
    [NotEmptyGuid] Guid UserId,
    [NotEmptyGuid] Guid VehicleId,
    [NotEmptyGuid] Guid ParkingSpotId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    [Range(0.01, 100_000, ErrorMessage = "Price must be a positive amount no greater than 100,000.")]
    decimal Price,
    [NotEmptyGuid] Guid IdempotencyKey) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndTime <= StartTime)
        {
            yield return new ValidationResult(
                "EndTime must be after StartTime.",
                [nameof(EndTime), nameof(StartTime)]);
        }

        if (StartTime < DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            yield return new ValidationResult(
                "StartTime cannot be in the past.",
                [nameof(StartTime)]);
        }

        if (EndTime - StartTime > TimeSpan.FromDays(30))
        {
            yield return new ValidationResult(
                "A reservation cannot span more than 30 days.",
                [nameof(StartTime), nameof(EndTime)]);
        }
    }
}
