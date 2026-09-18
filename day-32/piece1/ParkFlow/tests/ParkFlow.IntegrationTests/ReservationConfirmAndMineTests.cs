using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkFlow.Api.Security;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Day 32: <c>Confirm</c> and <c>GetMine</c> were added specifically so the frontend has a
/// reachable Pending → Confirmed → CheckedIn → Completed path and a way to show "my bookings" —
/// see ReservationApplicationService.cs's doc comments for why neither existed before this piece.
/// Uses random parking-spot ids throughout, same as the existing ownership/mutation-auth tests in
/// this project: Reservation and Parking are separate bounded contexts with no cross-module id
/// validation, so a real seeded spot is never required to exercise the Reservation module alone.
/// </summary>
public class ReservationConfirmAndMineTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret";

    private static HttpClient ClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);
        return client;
    }

    private static async Task<string> GetDevTokenAsync(HttpClient client, string user)
    {
        var response = await client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest(user));
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<Guid> CreateReservationAsync(HttpClient client, Guid ownerId, decimal price = 12.50m)
    {
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = ownerId,
            vehicleId = Guid.NewGuid(),
            parkingSpotId = Guid.NewGuid(),
            startTime = now,
            endTime = now.AddHours(2),
            price,
            idempotencyKey = Guid.NewGuid(),
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("reservationId").GetGuid();
    }

    [Fact]
    public async Task Confirm_AsOwner_Succeeds()
    {
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client, DemoUsers.UserA);
        var token = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_AsDifferentUser_Returns403()
    {
        var creatingClient = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(creatingClient, DemoUsers.UserA);

        var attackerClient = ClientWithApiKey(factory);
        var attackerToken = await GetDevTokenAsync(attackerClient, "userB");
        attackerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", attackerToken);

        var response = await attackerClient.PostAsync($"/api/v1/reservations/{reservationId}/confirm", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_ThenConfirmAgain_SecondCallIs400NotAnAuthFailure()
    {
        var client = ClientWithApiKey(factory);
        var reservationId = await CreateReservationAsync(client, DemoUsers.UserA);
        var token = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", content: null);
        var second = await client.PostAsync($"/api/v1/reservations/{reservationId}/confirm", content: null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyTheCallingUsersReservations_WithPriceAndStatus()
    {
        // This class shares one WebApplicationFactory (one in-memory DB) across every test method,
        // and several sibling tests also create Reservations owned by DemoUsers.UserA — so this
        // asserts the specific ids it cares about are present/absent, never "the array has exactly
        // N items", which would be flaky depending on test execution order.
        var client = ClientWithApiKey(factory);
        var mineId = await CreateReservationAsync(client, DemoUsers.UserA, price: 42.00m);
        var otherUsersId = await CreateReservationAsync(client, DemoUsers.UserB);
        var token = await GetDevTokenAsync(client, "userA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/reservations/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var all = body.RootElement.EnumerateArray().ToList();
        var mine = all.Single(r => r.GetProperty("id").GetGuid() == mineId);

        Assert.Equal(42.00m, mine.GetProperty("price").GetDecimal());
        Assert.Equal("Pending", mine.GetProperty("status").GetString());
        Assert.DoesNotContain(all, r => r.GetProperty("id").GetGuid() == otherUsersId);
    }

    [Fact]
    public async Task GetMine_WithoutAuth_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/reservations/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
