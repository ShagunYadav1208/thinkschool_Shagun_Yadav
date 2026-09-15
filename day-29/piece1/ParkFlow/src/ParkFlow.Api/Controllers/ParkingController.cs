using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Modules.Parking.Application.Availability;
using ParkFlow.Modules.Parking.Application.Spots;

namespace ParkFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/parking")]
public sealed class ParkingController(
    ParkingAvailabilityQueryService availability,
    ParkingSpotApplicationService spots) : ControllerBase
{
    /// <summary>Cache-aside read (see README, "Caching Design") — never writes application state.</summary>
    [HttpGet("facilities/{facilityId:guid}/availability")]
    public async Task<ActionResult<ParkingAvailabilitySnapshot>> GetAvailability(Guid facilityId, CancellationToken cancellationToken) =>
        await availability.GetAvailabilityAsync(facilityId, cancellationToken);

    [HttpPost("spots/{spotId:guid}/release")]
    public async Task<IActionResult> ReleaseSpot(Guid spotId, CancellationToken cancellationToken)
    {
        var result = await spots.ReleaseAsync(spotId, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }
}
