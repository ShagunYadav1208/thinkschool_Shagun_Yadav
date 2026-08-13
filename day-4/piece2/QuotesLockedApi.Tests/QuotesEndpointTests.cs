using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace QuotesLockedApi.Tests;

public sealed class QuotesEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // Matches appsettings.json's "Jwt" section so this test can mint tokens the app will accept,
    // including ones IssueInternalTokenAsync-style helpers can't produce (e.g. missing claims).
    private const string JwtIssuer = "QuotesLockedApi.Internal";
    private const string JwtAudience = "QuotesLockedApi.Internal.Callers";
    private const string JwtKey = "development-only-32-byte-minimum-jwt-signing-key!";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Root_ReturnsOkWithAuthAndPolicyInfo()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RootResponse>();
        Assert.NotNull(body);
        Assert.Contains("InternalJwt", body!.Auth);
        Assert.Contains("EntraJwt", body.Auth);
        Assert.Contains("can-edit-quotes", body.Policies);
    }

    [Fact]
    public async Task GetQuotes_ReturnsSeededQuotes()
    {
        var response = await _client.GetAsync("/quotes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quotes = await response.Content.ReadFromJsonAsync<List<QuoteDto>>();
        Assert.NotNull(quotes);
        Assert.Contains(quotes!, q => q.Id == 1);
    }

    [Fact]
    public async Task CreateQuote_WithWritePolicy_ReturnsCreatedQuoteOwnedByCaller()
    {
        var token = IssueInternalToken("user-123", ["quotes.write"]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/quotes")
        {
            Content = JsonContent.Create(new { text = "A new quote from the writer." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<QuoteDto>();
        Assert.NotNull(created);
        Assert.Equal("A new quote from the writer.", created!.Text);
        Assert.Equal("user-123", created.OwnerId);
    }

    [Fact]
    public async Task CreateQuote_WithTokenMissingSubjectClaim_ReturnsServerError()
    {
        // Every token TokenService issues carries a subject claim, so this deliberately mints one
        // without it to prove GetUserId's "no subject claim" guard actually fires on a real request
        // instead of only in theory.
        var token = IssueInternalToken(subject: null, scopes: ["quotes.write"]);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/quotes")
        {
            Content = JsonContent.Create(new { text = "Should never be created." })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UpdateQuote_WhenNotFound_ReturnsNotFound()
    {
        var token = IssueInternalToken("user-123", ["quotes.write"]);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/quotes/999999")
        {
            Content = JsonContent.Create(new { text = "no such quote" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string IssueInternalToken(string? subject, string[] scopes)
    {
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        claims.AddRange(scopes.Select(scope => new Claim("scope", scope)));

        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record RootResponse(
        [property: JsonPropertyName("auth")] string[] Auth,
        [property: JsonPropertyName("policies")] string[] Policies);

    private sealed record QuoteDto(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("ownerId")] string OwnerId);
}
