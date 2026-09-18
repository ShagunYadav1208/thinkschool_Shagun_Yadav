using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkFlow.Api.Security;
using ParkFlow.Modules.Identity.Application.Identity;

namespace ParkFlow.Api.Controllers;

/// <summary>
/// Replaces the old Development-only /api/v1/dev/token endpoint: real accounts, real passwords,
/// hashed at rest (see IdentityApplicationService). Both actions are anonymous by design — you
/// need an account before you can have a token at all.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
public sealed class AuthController(IdentityApplicationService identity, JwtTokenService tokens) : ControllerBase
{
    [HttpPost("register")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await identity.RegisterAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return BadRequest(new { error = result.Error });
        }

        var (token, expiresAt) = tokens.IssueToken(result.Value!.Id);
        return StatusCode(StatusCodes.Status201Created, new
        {
            userId = result.Value.Id,
            email = result.Value.Email,
            token,
            expiresAt,
        });
    }

    [HttpPost("login")]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await identity.LoginAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return Unauthorized(new { error = result.Error });
        }

        var (token, expiresAt) = tokens.IssueToken(result.Value!.Id);
        return Ok(new
        {
            userId = result.Value.Id,
            email = result.Value.Email,
            token,
            expiresAt,
        });
    }
}
