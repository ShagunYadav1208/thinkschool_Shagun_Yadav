using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace QuotesLockedApi.Tests;

public sealed class DeleteOwnQuoteHandlerTests
{
    private readonly InMemoryQuoteStore _quotes = new();

    [Fact]
    public async Task HandleRequirementAsync_UserIsOwner_Succeeds()
    {
        var handler = new DeleteOwnQuoteHandler(CreateAccessor("1"), _quotes);
        var context = CreateContext("user-123");

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_UserIsNotOwner_DoesNotSucceed()
    {
        var handler = new DeleteOwnQuoteHandler(CreateAccessor("1"), _quotes);
        var context = CreateContext("user-456");

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_MissingUserIdClaim_DoesNotSucceed()
    {
        var handler = new DeleteOwnQuoteHandler(CreateAccessor("1"), _quotes);
        var context = CreateContext(userId: null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_NonNumericRouteValue_DoesNotSucceed()
    {
        var handler = new DeleteOwnQuoteHandler(CreateAccessor("not-a-number"), _quotes);
        var context = CreateContext("user-123");

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(string? userId)
    {
        var claims = userId is null
            ? []
            : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        return new AuthorizationHandlerContext([new DeleteOwnQuoteRequirement()], user, resource: null);
    }

    private static IHttpContextAccessor CreateAccessor(string routeQuoteId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["quoteId"] = routeQuoteId;
        return new HttpContextAccessor { HttpContext = httpContext };
    }
}
