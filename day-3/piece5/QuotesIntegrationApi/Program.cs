using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesIntegrationApi.Abstractions;
using QuotesIntegrationApi.Data;
using QuotesIntegrationApi.Models;

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "QuotesIntegrationApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "QuotesIntegrationApi.Client";
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key configuration is required.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes for HMAC signing.");
}

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=quotes-integration.db"));

builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.NameIdentifier
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Day 3 Piece 5: WebApplicationFactory integration tests.",
    endpoints = new[]
    {
        "POST /auth/token",
        "GET /api/quotes",
        "GET /api/quotes/{id}",
        "POST /api/quotes",
        "DELETE /api/quotes/{id}"
    }
}));

app.MapPost("/auth/token", (TokenRequest request) =>
{
    var token = CreateToken(request.Subject, jwtIssuer, jwtAudience, jwtKey);
    return Results.Ok(new { access_token = token, token_type = "Bearer" });
});

var quotes = app.MapGroup("/api/quotes");

quotes.MapGet("/", async (
    int? page,
    int? size,
    QuotesDbContext db,
    CancellationToken cancellationToken) =>
{
    var currentPage = page ?? 1;
    var pageSize = size ?? 10;

    if (currentPage < 1 || pageSize < 1 || pageSize > 100)
    {
        var errors = new Dictionary<string, string[]>
        {
            ["page"] = currentPage < 1
                ? ["Page must be greater than 0."]
                : [],
            ["size"] = pageSize is < 1 or > 100
                ? ["Size must be between 1 and 100."]
                : []
        };

        return Results.ValidationProblem(errors);
    }

    var results = await db.Quotes
        .AsNoTracking()
        .OrderBy(q => q.Id)
        .Skip((currentPage - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(cancellationToken);

    return Results.Ok(results);
});

quotes.MapGet("/{id:int}", async (
    int id,
    QuotesDbContext db,
    CancellationToken cancellationToken) =>
{
    var quote = await db.Quotes
        .AsNoTracking()
        .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    return quote is null ? Results.NotFound() : Results.Ok(quote);
});

quotes.MapPost("/", async (
    CreateQuoteRequest request,
    QuotesDbContext db,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.Author))
        errors["author"] = ["Author is required."];
    else if (request.Author.Length > 100)
        errors["author"] = ["Author must be 100 characters or fewer."];

    if (string.IsNullOrWhiteSpace(request.Text))
        errors["text"] = ["Text is required."];
    else if (request.Text.Length > 1000)
        errors["text"] = ["Text must be 1000 characters or fewer."];

    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var quote = new QuotesIntegrationApi.Models.Quote
    {
        Author = request.Author.Trim(),
        Text = request.Text.Trim(),
        CreatedAt = clock.UtcNow
    };

    db.Quotes.Add(quote);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/quotes/{quote.Id}", quote);
})
    .RequireAuthorization();

quotes.MapDelete("/{id:int}", async (
    int id,
    QuotesDbContext db,
    CancellationToken cancellationToken) =>
{
    var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    if (quote is null)
        return Results.NotFound();

    db.Quotes.Remove(quote);
    await db.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
})
    .RequireAuthorization();

app.Run();

static string CreateToken(string subject, string issuer, string audience, string key)
{
    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, subject),
        new Claim(ClaimTypes.NameIdentifier, subject)
    };

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

public partial class Program;
