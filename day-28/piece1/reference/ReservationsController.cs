using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Modules.Reservation.Application.Reservations;

namespace ParkFlow.Api.Controllers;

/// <summary>
/// Thin by design: every method here does argument mapping and status-code translation only.
/// The actual rules (state transitions, idempotency, overlap checks) live in
/// <see cref="ReservationApplicationService"/> and the Reservation aggregate itself.
///
/// Day 27: none of these actions check that the caller owns the reservation being
/// cancelled/checked-in/completed — see THREAT-MODEL.md section 3.1 (Elevation of Privilege).
/// Not fixed this pass; documented rather than silently left as-is.
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
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CancelAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CheckInAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CompleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }
}
