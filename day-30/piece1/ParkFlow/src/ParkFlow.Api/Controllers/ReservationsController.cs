using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Api.Security;
using ParkFlow.BuildingBlocks.Application;
using ParkFlow.Modules.Reservation.Application.Reservations;

namespace ParkFlow.Api.Controllers;

/// <summary>
/// Thin by design: every method here does argument mapping and status-code translation only.
/// The actual rules (state transitions, idempotency, overlap checks, and — as of Build Day 30 —
/// the ownership guard) live in <see cref="ReservationApplicationService"/> and the Reservation
/// aggregate itself. See THREAT-MODEL.md section 3.1 (Elevation of Privilege, day-27/piece1) for
/// the gap this closes, and ADR-001 (day-28/piece1) for the design.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reservations")]
public sealed class ReservationsController(ReservationApplicationService reservations) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Create(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var result = await reservations.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Create), new { id = result.Value }, new { reservationId = result.Value })
            : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = ReservationMutationPolicy.Name)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CancelAsync(id, GetCurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Policy = ReservationMutationPolicy.Name)]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CheckInAsync(id, GetCurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = ReservationMutationPolicy.Name)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CompleteAsync(id, GetCurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    // Safe to assume present and well-formed: ReservationMutationPolicy already required a
    // successfully-validated Bearer token (sub claim mapped to NameIdentifier) before any of the
    // three actions above run.
    private Guid GetCurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult ToActionResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return result.ErrorKind switch
        {
            ResultErrorKind.NotFound => NotFound(new { error = result.Error }),
            ResultErrorKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Error }),
            _ => BadRequest(new { error = result.Error }),
        };
    }
}
