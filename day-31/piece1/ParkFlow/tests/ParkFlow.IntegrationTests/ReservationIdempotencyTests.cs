using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkFlow.Api.Security;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Day 31 perf pass: ReservationRepository.GetByIdempotencyKeyAsync now answers from
/// ReservationIdempotencyIndex (an in-process dictionary) instead of an EF predicate query — see
/// that class and day-31/piece1/README.md's perf section. These tests are the functional safety
/// net for that change: the *behavior* a client sees (retry-with-same-key returns the original
/// reservation; a fresh key creates a distinct one) must be identical to before, even though the
/// lookup path underneath it is now entirely different.
/// </summary>
public class ReservationIdempotencyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret"; // matches appsettings.Development.json

    private static async Task<Guid> CreateReservationAsync(HttpClient client, Guid idempotencyKey)
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
            idempotencyKey,
        });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("reservationId").GetGuid();
    }

    [Fact]
    public async Task Create_RetriedWithSameIdempotencyKey_ReturnsTheSameReservationId()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);
        var idempotencyKey = Guid.NewGuid();

        var first = await CreateReservationAsync(client, idempotencyKey);
        var retried = await CreateReservationAsync(client, idempotencyKey);

        Assert.Equal(first, retried);
    }

    [Fact]
    public async Task Create_WithDifferentIdempotencyKeys_CreatesDistinctReservations()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var first = await CreateReservationAsync(client, Guid.NewGuid());
        var second = await CreateReservationAsync(client, Guid.NewGuid());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Create_RetriedAfterManyOtherReservationsExist_StillReturnsTheOriginal()
    {
        // The index (not a table scan) is what answers this now — prove it still finds the right
        // reservation once plenty of *other* keys are already recorded, not just in isolation.
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);
        var idempotencyKey = Guid.NewGuid();
        var first = await CreateReservationAsync(client, idempotencyKey);

        for (var i = 0; i < 20; i++)
        {
            await CreateReservationAsync(client, Guid.NewGuid());
        }

        var retried = await CreateReservationAsync(client, idempotencyKey);

        Assert.Equal(first, retried);
    }
}
