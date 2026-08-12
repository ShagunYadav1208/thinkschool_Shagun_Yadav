using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesIntegrationApi.Data;

namespace Quotes.Tests.Integration;

public sealed class QuotesEndpointTests : IAsyncLifetime
{
    private QuotesApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new QuotesApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // --- happy path (paste #1) ---
    [Fact]
    public async Task CreateQuote_WithValidTokenAndBody_ReturnsCreatedAndStampsFakeClock()
    {
        var token = await IssueTokenAsync("writer-1");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { author = "Ada Lovelace", text = "That brain of mine is something more than merely mortal." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(created);
        Assert.Equal("Ada Lovelace", created!.Author);
        Assert.Equal(_factory.Clock.UtcNow, created.CreatedAt);
    }

    // --- error path (paste #2) ---
    [Fact]
    public async Task CreateQuote_WithBlankText_ReturnsValidationProblem()
    {
        var token = await IssueTokenAsync("writer-1");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { author = "Ada Lovelace", text = "   " })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("text"));
    }

    [Fact]
    public async Task GetQuotes_WhenDatabaseIsEmpty_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.NotNull(quotes);
        Assert.Empty(quotes!);
    }

    [Fact]
    public async Task GetQuotes_WithPageBelowOne_ReturnsValidationProblem()
    {
        var response = await _client.GetAsync("/api/quotes?page=0&size=10");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("page"));
    }

    [Fact]
    public async Task GetQuotes_WithSizeAboveMaximum_ReturnsValidationProblem()
    {
        var response = await _client.GetAsync("/api/quotes?page=1&size=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("size"));
    }

    [Fact]
    public async Task GetQuoteById_WhenMissing_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/quotes/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetQuoteById_WhenExists_ReturnsQuote()
    {
        var seeded = await CreateQuoteAsync("Grace Hopper", "It's easier to ask forgiveness than permission.");

        var response = await _client.GetAsync($"/api/quotes/{seeded.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(fetched);
        Assert.Equal(seeded.Id, fetched!.Id);
        Assert.Equal("Grace Hopper", fetched.Author);
    }

    [Fact]
    public async Task CreateQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/quotes", new { author = "Anonymous", text = "No token, no write." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithAuthorTooLong_ReturnsValidationProblem()
    {
        var token = await IssueTokenAsync("writer-1");
        var overlongAuthor = new string('a', 101);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { author = overlongAuthor, text = "Valid text." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.True(problem!.Errors.ContainsKey("author"));
    }

    [Fact]
    public async Task DeleteQuote_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.DeleteAsync("/api/quotes/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_WithTokenWhenExists_ReturnsNoContentAndRemovesIt()
    {
        var seeded = await CreateQuoteAsync("Margaret Hamilton", "There was no choice but to be pioneers.");
        var token = await IssueTokenAsync("writer-1");

        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/quotes/{seeded.Id}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/quotes/{seeded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_WithTokenWhenMissing_ReturnsNotFound()
    {
        var token = await IssueTokenAsync("writer-1");

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/quotes/999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Migrations_AreAppliedAtStartup_QuotesTableIsQueryable()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Contains("InitialCreate", applied.Select(m => m.Split('_', 2)[1]));

        var quoteCount = await db.Quotes.CountAsync();
        Assert.Equal(0, quoteCount);
    }

    private async Task<string> IssueTokenAsync(string subject)
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new { subject });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return body?.AccessToken ?? throw new InvalidOperationException("Token response was empty.");
    }

    private async Task<QuoteDto> CreateQuoteAsync(string author, string text)
    {
        var token = await IssueTokenAsync("seed-user");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = JsonContent.Create(new { author, text })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
        return created ?? throw new InvalidOperationException("Create response was empty.");
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record QuoteDto(int Id, string Author, string Text, DateTimeOffset CreatedAt);
}
