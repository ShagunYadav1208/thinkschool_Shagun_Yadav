using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkFlow.Api.Security;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Day 32: proves the seed data and the new read/mutation endpoints the frontend depends on
/// (facilities/spots list, reserve/occupy/release) actually work — added alongside the frontend
/// itself, not after the fact. Each test claims its own spot by number so tests in this class don't
/// interfere with each other regardless of execution order.
/// </summary>
public class ParkingDirectoryTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret";

    private static HttpClient ClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);
        return client;
    }

    private static async Task<JsonElement> GetSeededFacilityAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/parking/facilities");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().First().Clone();
    }

    private static async Task<Guid> GetSpotIdByNumberAsync(HttpClient client, Guid facilityId, string spotNumber)
    {
        var response = await client.GetAsync($"/api/v1/parking/facilities/{facilityId}/spots");
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var match = body.RootElement.EnumerateArray().First(s => s.GetProperty("spotNumber").GetString() == spotNumber);
        return match.GetProperty("id").GetGuid();
    }

    private static async Task<string> GetSpotStatusAsync(HttpClient client, Guid facilityId, Guid spotId)
    {
        var response = await client.GetAsync($"/api/v1/parking/facilities/{facilityId}/spots");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var match = body.RootElement.EnumerateArray().First(s => s.GetProperty("id").GetGuid() == spotId);
        return match.GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task ListFacilities_ReturnsTheSeededDowntownGarage()
    {
        var client = ClientWithApiKey(factory);

        var facility = await GetSeededFacilityAsync(client);

        Assert.Equal("Downtown Garage", facility.GetProperty("name").GetString());
        Assert.Equal(2, facility.GetProperty("floorCount").GetInt32());
    }

    [Fact]
    public async Task ListSpots_ReturnsNineSeededSpots()
    {
        var client = ClientWithApiKey(factory);
        var facility = await GetSeededFacilityAsync(client);

        var response = await client.GetAsync($"/api/v1/parking/facilities/{facility.GetProperty("id").GetGuid()}/spots");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(9, body.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ReserveSpot_TransitionsAvailableToReserved()
    {
        var client = ClientWithApiKey(factory);
        var facility = await GetSeededFacilityAsync(client);
        var facilityId = facility.GetProperty("id").GetGuid();
        var spotId = await GetSpotIdByNumberAsync(client, facilityId, "A1");

        var response = await client.PostAsync($"/api/v1/parking/spots/{spotId}/reserve", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Reserved", await GetSpotStatusAsync(client, facilityId, spotId));
    }

    [Fact]
    public async Task OccupySpot_WithoutReservingFirst_Returns400()
    {
        var client = ClientWithApiKey(factory);
        var facility = await GetSeededFacilityAsync(client);
        var spotId = await GetSpotIdByNumberAsync(client, facility.GetProperty("id").GetGuid(), "A2");

        var response = await client.PostAsync($"/api/v1/parking/spots/{spotId}/occupy", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReserveThenOccupyThenRelease_CyclesBackToAvailable()
    {
        var client = ClientWithApiKey(factory);
        var facility = await GetSeededFacilityAsync(client);
        var facilityId = facility.GetProperty("id").GetGuid();
        var spotId = await GetSpotIdByNumberAsync(client, facilityId, "A3");

        await client.PostAsync($"/api/v1/parking/spots/{spotId}/reserve", content: null);
        var occupy = await client.PostAsync($"/api/v1/parking/spots/{spotId}/occupy", content: null);
        Assert.Equal(HttpStatusCode.NoContent, occupy.StatusCode);
        Assert.Equal("Occupied", await GetSpotStatusAsync(client, facilityId, spotId));

        var release = await client.PostAsync($"/api/v1/parking/spots/{spotId}/release", content: null);
        Assert.Equal(HttpStatusCode.NoContent, release.StatusCode);
        Assert.Equal("Available", await GetSpotStatusAsync(client, facilityId, spotId));
    }

    [Fact]
    public async Task Endpoints_StillRequireTheApiKey()
    {
        var client = factory.CreateClient();

        var facilities = await client.GetAsync("/api/v1/parking/facilities");
        var reserve = await client.PostAsync($"/api/v1/parking/spots/{Guid.NewGuid()}/reserve", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, facilities.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, reserve.StatusCode);
    }
}
