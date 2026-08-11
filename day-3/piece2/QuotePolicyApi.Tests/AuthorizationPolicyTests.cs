using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QuotePolicyApi.Tests;

public sealed class AuthorizationPolicyTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task EditQuoteWithoutWriteScope_ReturnsForbidden()
    {
        var token = await IssueTokenAsync("user-123", "quotes.read");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/1")
        {
            Content = JsonContent.Create(new { text = "Updated quote" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuoteOwnedBySomeoneElse_ReturnsForbidden()
    {
        var token = await IssueTokenAsync("user-999", "quotes.write");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditQuoteWithWriteScope_ReturnsOk()
    {
        var token = await IssueTokenAsync("user-123", "quotes.write");

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/1")
        {
            Content = JsonContent.Create(new { text = "Updated quote" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOwnQuote_ReturnsOk()
    {
        var token = await IssueTokenAsync("user-123", "quotes.read");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> IssueTokenAsync(string subject, string scope)
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new { subject, scope });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body?.AccessToken ?? throw new InvalidOperationException("Token response was empty.");
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
}
