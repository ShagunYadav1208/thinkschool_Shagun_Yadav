using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QuotesLockedApi.Tests;

public sealed class AuthIntegrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AnonymousMutatingRequest_ReturnsUnauthorized()
    {
        var response = await _client.PutAsJsonAsync("/quotes/1", new { text = "anonymous edit" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedCallerWithWrongPolicy_ReturnsForbidden()
    {
        var token = await IssueInternalTokenAsync("user-456", ["quotes.read"]);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/1")
        {
            Content = JsonContent.Create(new { text = "reader edit" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedCallerWithRightPolicy_ReturnsOk()
    {
        var token = await IssueInternalTokenAsync("user-123", ["quotes.write"]);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/1")
        {
            Content = JsonContent.Create(new { text = "writer edit" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessToken_ReturnsUnauthorized()
    {
        var token = await IssueInternalTokenAsync("user-123", ["quotes.write"], expiresInSeconds: -60);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/1")
        {
            Content = JsonContent.Create(new { text = "expired edit" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReusedRefreshToken_RevokesRefreshChain()
    {
        var login = await LoginAsync("writer@quotes.local", "WriterPassword123!");

        var firstRefresh = await _client.PostAsJsonAsync("/auth/refresh", new { login.RefreshToken });
        firstRefresh.EnsureSuccessStatusCode();
        var rotated = await ReadTokenResponseAsync(firstRefresh);

        var reuseOldToken = await _client.PostAsJsonAsync("/auth/refresh", new { login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseOldToken.StatusCode);

        var useRevokedReplacement = await _client.PostAsJsonAsync("/auth/refresh", new { rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, useRevokedReplacement.StatusCode);
    }

    [Fact]
    public async Task DeleteOwnQuote_WithCustomPolicy_ReturnsNoContent()
    {
        var token = await IssueInternalTokenAsync("user-123", ["quotes.read"]);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_ByNonOwner_ReturnsForbidden()
    {
        // Quote 1 is seeded as owned by user-123; user-456 must not be able to delete it.
        var token = await IssueInternalTokenAsync("user-456", ["quotes.write"]);

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/quotes/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email = "writer@quotes.local", password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/login",
            new { email = "nobody@quotes.local", password = "whatever" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithNeverIssuedToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/refresh",
            new { refreshToken = "this-token-was-never-issued" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InternalToken_WithWrongClientSecret_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/internal-token", new
        {
            subject = "user-123",
            clientSecret = "not-the-secret",
            scopes = (string[]?)null,
            expiresInSeconds = (int?)null
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InternalToken_WithUnknownSubject_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/auth/internal-token", new
        {
            subject = "no-such-user",
            clientSecret = "local-dev-secret",
            scopes = (string[]?)null,
            expiresInSeconds = (int?)null
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> IssueInternalTokenAsync(string subject, string[] scopes, int? expiresInSeconds = null)
    {
        var response = await _client.PostAsJsonAsync("/auth/internal-token", new
        {
            subject,
            clientSecret = "local-dev-secret",
            scopes,
            expiresInSeconds
        });
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        return token?.AccessToken ?? throw new InvalidOperationException("Token response was empty.");
    }

    private async Task<TokenResponse> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return await ReadTokenResponseAsync(response);
    }

    private static async Task<TokenResponse> ReadTokenResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body ?? throw new InvalidOperationException("Token response was empty.");
    }

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken);

    private sealed record TokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
