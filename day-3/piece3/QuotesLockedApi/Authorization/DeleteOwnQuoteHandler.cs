using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace QuotesLockedApi;

public sealed class DeleteOwnQuoteHandler(
    IHttpContextAccessor httpContextAccessor,
    IQuoteStore quotes) : AuthorizationHandler<DeleteOwnQuoteRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DeleteOwnQuoteRequirement requirement)
    {
        var userId = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var routeValue = httpContextAccessor.HttpContext?.Request.RouteValues["quoteId"]?.ToString();

        if (userId is not null
            && int.TryParse(routeValue, out var quoteId)
            && quotes.IsOwner(quoteId, userId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
