using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Modules.Parking.Application.Availability;
using ParkFlow.Modules.Parking.Application.Facilities;
using ParkFlow.Modules.Parking.Application.Spots;

namespace ParkFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/parking")]
public sealed class ParkingController(
    ParkingAvailabilityQueryService availability,
    ParkingSpotApplicationService spots,
    ParkingFacilityApplicationService facilities) : ControllerBase
{
    /// <summary>Day 32: the frontend's facility picker. API-key only — no ownership concept for a facility list.</summary>
    [HttpGet("facilities")]
    public async Task<ActionResult<IReadOnlyList<ParkingFacilitySummary>>> ListFacilities(CancellationToken cancellationToken) =>
        Ok(await facilities.ListAllAsync(cancellationToken));

    /// <summary>Day 32: the frontend's spot grid for one facility.</summary>
    [HttpGet("facilities/{facilityId:guid}/spots")]
    public async Task<ActionResult<IReadOnlyList<ParkingSpotSummary>>> ListSpots(Guid facilityId, CancellationToken cancellationToken) =>
        Ok(await spots.ListByFacilityAsync(facilityId, cancellationToken));

    /// <summary>Cache-aside read (see README, "Caching Design") — never writes application state.</summary>
    [HttpGet("facilities/{facilityId:guid}/availability")]
    public async Task<ActionResult<ParkingAvailabilitySnapshot>> GetAvailability(Guid facilityId, CancellationToken cancellationToken) =>
        await availability.GetAvailabilityAsync(facilityId, cancellationToken);

    /// <summary>
    /// Day 32: exposed so the frontend can hold a spot the moment a booking is made. Existed at the
    /// application layer since day-22/piece2 (see <see cref="ParkingSpotApplicationService"/>'s own
    /// doc comment on why this is a direct call rather than a message-broker consumer reacting to
    /// ReservationCreated) but had no controller action until now.
    /// </summary>
    [HttpPost("spots/{spotId:guid}/reserve")]
    public async Task<IActionResult> ReserveSpot(Guid spotId, CancellationToken cancellationToken)
    {
        var result = await spots.ReserveAsync(spotId, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Day 32: the frontend calls this on check-in, same scaffolding rationale as ReserveSpot above.</summary>
    [HttpPost("spots/{spotId:guid}/occupy")]
    public async Task<IActionResult> OccupySpot(Guid spotId, CancellationToken cancellationToken)
    {
        var result = await spots.OccupyAsync(spotId, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("spots/{spotId:guid}/release")]
    public async Task<IActionResult> ReleaseSpot(Guid spotId, CancellationToken cancellationToken)
    {
        var result = await spots.ReleaseAsync(spotId, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }
}
