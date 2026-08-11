using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace QuotePolicyApi;

public sealed class OwnQuoteAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    IQuoteOwnershipService ownership) : AuthorizationHandler<OwnQuoteRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnQuoteRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (httpContext?.Request.RouteValues["quoteId"] is not null
            && int.TryParse(httpContext.Request.RouteValues["quoteId"]?.ToString(), out var quoteId)
            && userId is not null
            && ownership.IsOwner(quoteId, userId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
