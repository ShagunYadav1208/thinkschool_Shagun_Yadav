using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Modules.Vehicle.Application.Vehicles;

namespace ParkFlow.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/vehicles")]
public sealed class VehiclesController(VehicleApplicationService vehicles) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Register(RegisterVehicleRequest request, CancellationToken cancellationToken)
    {
        var result = await vehicles.RegisterAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Register), new { id = result.Value }, new { vehicleId = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
