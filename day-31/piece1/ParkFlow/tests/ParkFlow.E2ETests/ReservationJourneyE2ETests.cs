using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ParkFlow.Api.Security;

namespace ParkFlow.E2ETests;

/// <summary>
/// Day 31 (Testing pyramid): the one true end-to-end test — a full user journey against the real
/// compiled app running as an actual child process on a real loopback port (see
/// <see cref="RealApiProcessFixture"/>), not the in-memory TestServer the unit/integration layers
/// use. One test, one journey: create a reservation, prove a stranger can't touch it, then prove
/// the owner can. If this were the only test in the whole suite, it would still prove the
/// capstone's core promise (ADR-001, day-28/piece1) actually holds for a real client.
/// </summary>
public class ReservationJourneyE2ETests : IClassFixture<RealApiProcessFixture>
{
    private readonly HttpClient _client;

    public ReservationJourneyE2ETests(RealApiProcessFixture fixture)
    {
        _client = fixture.Client;
        _client.DefaultRequestHeaders.Add("X-Api-Key", RealApiProcessFixture.ApiKey);
    }

    [Fact]
    public async Task FullReservationJourney_CreateThenBlockAStrangerThenLetTheOwnerCancel()
    {
        // 1. The app is actually up, over a real socket.
        var health = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // 2. userA creates a reservation.
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var createResponse = await _client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = DemoUsers.UserA,
            vehicleId = Guid.NewGuid(),
            parkingSpotId = Guid.NewGuid(),
            startTime = now,
            endTime = now.AddHours(2),
            price = 12.50m,
            idempotencyKey = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createdBody = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var reservationId = createdBody.RootElement.GetProperty("reservationId").GetGuid();

        // 3. userB (a stranger to this reservation) gets a real token and tries to cancel it.
        var userBToken = await GetDevTokenAsync("userB");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userBToken);
        var strangerAttempt = await _client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, strangerAttempt.StatusCode);

        // 4. userA, the actual owner, cancels it.
        var userAToken = await GetDevTokenAsync("userA");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAToken);
        var ownerCancel = await _client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.NoContent, ownerCancel.StatusCode);

        // 5. Cancelling an already-cancelled reservation is a business-state failure, not an auth one.
        var secondCancel = await _client.PostAsync($"/api/v1/reservations/{reservationId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, secondCancel.StatusCode);
    }

    private async Task<string> GetDevTokenAsync(string user)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/dev/token", new DevTokenRequest(user));
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }
}
