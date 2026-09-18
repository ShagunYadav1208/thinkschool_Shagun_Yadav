using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace ParkFlow.Api.Security;

/// <summary>Name of the composite policy the three reservation-mutation routes require (ADR-001).</summary>
public static class ReservationMutationPolicy
{
    public const string Name = "ReservationMutation";
}

/// <summary>Marker requirement satisfied only when both the API key and the JWT bearer schemes authenticated.</summary>
public sealed class RequireApiKeyAndBearerRequirement : IAuthorizationRequirement;

/// <summary>
/// <c>[Authorize(AuthenticationSchemes = "ApiKey,Bearer")]</c> alone would NOT require both: ASP.NET
/// Core's policy evaluator authenticates every listed scheme and merges the identities of whichever
/// ones succeed into <see cref="AuthorizationHandlerContext.User"/>, and <c>RequireAuthenticatedUser()</c>
/// only checks that at least one of them did — so that attribute by itself is an OR, not an AND.
/// ADR-001 (day-28/piece1) needs both: the API key answers "which client is calling", the JWT
/// answers "on behalf of which user" — one without the other must not be enough. This handler checks
/// explicitly for an authenticated identity from each scheme instead of relying on the attribute's
/// default merge-and-check-any behavior. See ReservationMutationAuthTests.cs for the 403 this
/// produces when only one of the two is presented.
/// </summary>
public sealed class RequireApiKeyAndBearerHandler : AuthorizationHandler<RequireApiKeyAndBearerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RequireApiKeyAndBearerRequirement requirement)
    {
        var hasApiKey = context.User.Identities.Any(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme);
        var hasBearer = context.User.Identities.Any(identity =>
            identity.IsAuthenticated && identity.AuthenticationType == JwtBearerDefaults.AuthenticationScheme);

        if (hasApiKey && hasBearer)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
