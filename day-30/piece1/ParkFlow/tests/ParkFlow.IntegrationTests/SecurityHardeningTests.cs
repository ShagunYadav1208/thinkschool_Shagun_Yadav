using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ParkFlow.Api.Security;
using ParkFlow.Modules.Vehicle.Application.Vehicles;
using ParkFlow.Modules.Vehicle.Domain;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Proves the Day 27 hardening actually does something at runtime, not just that it compiles:
/// every endpoint requires the API key (except /health), and the request DTOs reject the
/// obviously-invalid input that used to reach the domain layer unchecked. See ../../THREAT-MODEL.md.
/// </summary>
public class SecurityHardeningTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DevApiKey = "dev-local-only-not-a-secret"; // matches appsettings.Development.json

    [Fact]
    public async Task Request_WithoutApiKey_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/vehicles",
            new RegisterVehicleRequest(Guid.NewGuid(), "ABC123", VehicleType.Car));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithWrongApiKey_Returns401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, "not-the-right-key");

        var response = await client.PostAsJsonAsync(
            "/api/v1/vehicles",
            new RegisterVehicleRequest(Guid.NewGuid(), "ABC123", VehicleType.Car));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_StillWorksWithoutApiKey()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task RegisterVehicle_WithValidKeyAndInput_Returns201()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/v1/vehicles",
            new RegisterVehicleRequest(Guid.NewGuid(), "ABC123", VehicleType.Car));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task RegisterVehicle_WithEmptyOwnerId_Returns400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/v1/vehicles",
            new RegisterVehicleRequest(Guid.Empty, "ABC123", VehicleType.Car));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterVehicle_WithOverlongLicensePlate_Returns400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var response = await client.PostAsJsonAsync(
            "/api/v1/vehicles",
            new RegisterVehicleRequest(Guid.NewGuid(), new string('X', 500), VehicleType.Car));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_WithEndBeforeStart_Returns400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var now = DateTimeOffset.UtcNow.AddHours(1);
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = Guid.NewGuid(),
            vehicleId = Guid.NewGuid(),
            parkingSpotId = Guid.NewGuid(),
            startTime = now,
            endTime = now.AddHours(-1),
            price = 10.00m,
            idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_WithNegativePrice_Returns400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, DevApiKey);

        var now = DateTimeOffset.UtcNow.AddHours(1);
        var response = await client.PostAsJsonAsync("/api/v1/reservations", new
        {
            userId = Guid.NewGuid(),
            vehicleId = Guid.NewGuid(),
            parkingSpotId = Guid.NewGuid(),
            startTime = now,
            endTime = now.AddHours(1),
            price = -5.00m,
            idempotencyKey = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesInclude_SecurityHeaders()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));
    }
}
