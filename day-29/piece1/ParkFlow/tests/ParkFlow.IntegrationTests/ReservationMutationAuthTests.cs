using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ParkFlow.Api.Security;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Day 29 (BUILD-PLAN.md, day-28/piece1): proves the new JWT bearer scheme and the composite
/// "ApiKey AND Bearer" policy actually reject requests at runtime — not just that they compile.
/// No ownership check exists yet (that's Build Day 30), so these only assert 401/403/pass-through,
/// never who the reservation belongs to.
/// </summary>
public class ReservationMutationAuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret"; // matches appsettings.Development.json

    private static async Task<string> GetDevTokenAsync(HttpClient client, string user = "userA")
    {
        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest(user));
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<Guid> CreateReservationAsync(HttpClient client)
    {
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = Guid.NewGuid(),
            vehicleId = Guid.NewGuid(),
            parkingSpotId = Guid.NewGuid(),
            startTime = now,
            endTime = now.AddHours(2),
            price = 10.00m,
            idempotencyKey = Guid.NewGuid(),
        });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("reservationId").GetGuid();
    }

    private static HttpClient ClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);
        return client;
    }

    [Fact]
    public async Task DevToken_WithKnownUser_ReturnsToken()
    {
        var client = factory.CreateClient();

        var token = await GetDevTokenAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task DevToken_WithUnknownUser_Returns400()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest("someoneElse"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("check-in")]
    [InlineData("complete")]
    public async Task MutationRoute_WithNoAuth_Returns401(string action)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/reservations/{Guid.NewGuid()}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("check-in")]
    [InlineData("complete")]
    public async Task MutationRoute_WithApiKeyOnly_Returns403(string action)
    {
        var client = ClientWithApiKey(factory);

        var response = await client.PostAsync($"/api/v1/reservations/{Guid.NewGuid()}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("check-in")]
    [InlineData("complete")]
    public async Task MutationRoute_WithBearerOnly_Returns403(string action)
    {
        var client = factory.CreateClient();
        var token = await GetDevTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/api/v1/reservations/{Guid.NewGuid()}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_WithApiKeyAndBearer_PassesAuthAndReachesTheApplicationService()
    {
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client);
        var token = await GetDevTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);

        // No ownership check yet (Build Day 30) — any valid demo user's token is enough. What this
        // proves is that supplying *both* schemes clears the composite policy: 204, not 401/403.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("check-in")]
    [InlineData("complete")]
    public async Task CheckInOrComplete_WithApiKeyAndBearer_PassesAuthAndReaches400FromBusinessRules(string action)
    {
        // A freshly-created reservation is Pending, and both CheckIn and Complete require
        // Confirmed/CheckedIn first (see ReservationTests.cs) — there's no HTTP route to Confirm
        // yet, so these always 400 on business grounds. That 400 (not 401/403) is exactly what
        // proves the composite policy let the request through to the application service.
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client);
        var token = await GetDevTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/api/v1/reservations/{reservationId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_StillOnlyRequiresApiKey_NoBearerNeeded()
    {
        var client = ClientWithApiKey(factory);

        var reservationId = await CreateReservationAsync(client);

        Assert.NotEqual(Guid.Empty, reservationId);
    }

    [Fact]
    public async Task DevTokenEndpoint_IsNotMappedOutsideDevelopment()
    {
        const string ProdApiKey = "prod-test-key-not-a-real-secret";
        await using var productionFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:ApiKey"] = ProdApiKey,
                    ["Security:Jwt:SigningKey"] = "prod-test-signing-key-not-a-real-secret-32-bytes-min",
                });
            });
        });
        var client = productionFactory.CreateClient();
        // A valid API key so the response can only be "route not found", not "fallback policy
        // rejected an unauthenticated caller" — the fallback policy (see Program.cs) applies to
        // *any* unmatched request, authenticated or not, so without this the 401 an unauthenticated
        // caller gets here would be indistinguishable from the endpoint genuinely not existing.
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, ProdApiKey);

        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest("userA"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
