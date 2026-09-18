using System.ComponentModel.DataAnnotations;

namespace ParkFlow.Modules.Identity.Application.Identity;

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);
