using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Api.Security;
using ParkFlow.Modules.Reservation.Application.Reservations;

namespace ParkFlow.Api.Controllers;

/// <summary>
/// Thin by design: every method here does argument mapping and status-code translation only.
/// The actual rules (state transitions, idempotency, overlap checks) live in
/// <see cref="ReservationApplicationService"/> and the Reservation aggregate itself.
///
/// Day 27: none of these actions check that the caller owns the reservation being
/// cancelled/checked-in/completed — see THREAT-MODEL.md section 3.1 (Elevation of Privilege).
/// Day 29 (ADR-001, day-28/piece1) adds the identity plumbing — the composite
/// <see cref="ReservationMutationPolicy"/> below — but not the ownership check itself yet: these
/// three actions now require both an API key and a JWT, from *any* valid demo user, before falling
/// through to the same unchanged application-service calls. The actual "is this your reservation?"
/// guard, and mapping a Result's error kind to 403/404, is Build Day 30.
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
        var result = await reservations.CancelAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Policy = ReservationMutationPolicy.Name)]
    public async Task<IActionResult> CheckIn(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CheckInAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = ReservationMutationPolicy.Name)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await reservations.CompleteAsync(id, cancellationToken);
        return result.IsSuccess ? NoContent() : BadRequest(new { error = result.Error });
    }
}
