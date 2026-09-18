using System.ComponentModel.DataAnnotations;

namespace ParkFlow.Modules.Identity.Application.Identity;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(254, MinimumLength = 3)] string Email,
    [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    string Password);
