using Microsoft.AspNetCore.Mvc.Testing;

namespace ParkFlow.IntegrationTests;

/// <summary>
/// Deliberately minimal for this piece: it exists to prove the whole composition root (five
/// modules' Application + Infrastructure wired together in Program.cs) actually boots, not to
/// cover business scenarios yet.
/// </summary>
public class HealthCheckTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }
}
