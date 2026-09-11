using System.ComponentModel.DataAnnotations;
using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.Modules.Vehicle.Application.Vehicles;

using VehicleType = ParkFlow.Modules.Vehicle.Domain.VehicleType;

public sealed record RegisterVehicleRequest(
    [NotEmptyGuid] Guid OwnerUserId,
    [Required, StringLength(15, MinimumLength = 1), RegularExpression(@"^[A-Za-z0-9\- ]+$",
        ErrorMessage = "License plate may only contain letters, digits, spaces, and hyphens.")]
    string LicensePlate,
    VehicleType VehicleType);
