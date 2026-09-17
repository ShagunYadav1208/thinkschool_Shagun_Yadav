using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkFlow.Api.Security;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Build Day 30 (BUILD-PLAN.md, day-28/piece1): closes the reservation-ownership/BOLA gap
/// THREAT-MODEL.md ranked #1 (day-27/piece1) — user B must not be able to cancel/check-in/complete
/// user A's reservation just by knowing its id. Day 29 proved the *auth* plumbing (need both an API
/// key and a JWT); this proves the actual ownership guard behind it.
/// </summary>
public class ReservationOwnershipTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret"; // matches appsettings.Development.json

    private static async Task<string> GetDevTokenAsync(HttpClient client, string user)
    {
        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest(user));
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<Guid> CreateReservationAsync(HttpClient client, Guid ownerId)
    {
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = ownerId,
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
    public async Task Cancel_AsOwner_Succeeds()
    {
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client, DemoUsers.UserA);
        var ownerToken = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var response = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AsDifferentUser_Returns403()
    {
        var creatingClient = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(creatingClient, DemoUsers.UserA);

        var attackerClient = ClientWithApiKey(factory);
        var attackerToken = await GetDevTokenAsync(attackerClient, "userB");
        attackerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await attackerClient.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("check-in")]
    [InlineData("complete")]
    public async Task CheckInOrComplete_AsDifferentUser_Returns403BeforeAnyStateCheck(string action)
    {
        // A freshly-created reservation is Pending, which would 400 on business grounds for
        // *anyone* attempting check-in/complete (see ReservationMutationAuthTests.cs). Getting 403
        // here instead of 400 is exactly what proves the ownership guard runs before the aggregate
        // is even touched — a non-owner learns nothing about the reservation's actual state.
        var creatingClient = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(creatingClient, DemoUsers.UserA);

        var attackerClient = ClientWithApiKey(factory);
        var attackerToken = await GetDevTokenAsync(attackerClient, "userB");
        attackerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await attackerClient.PostAsync($"/api/v1/reservations/{reservationId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_NonexistentReservation_Returns404NotForbidden()
    {
        var client = ClientWithApiKey(factory);
        var token = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/api/v1/reservations/{Guid.NewGuid()}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AsOwner_ErrorBodyMatchesResultError()
    {
        // Cancelling twice: the second call hits a real domain-state failure (Validation, not
        // Forbidden/NotFound) - proves the switch in ReservationsController.ToActionResult still
        // falls through to 400 for the default case, not just the two new branches.
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client, DemoUsers.UserA);
        var token = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);
        var second = await client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
}
