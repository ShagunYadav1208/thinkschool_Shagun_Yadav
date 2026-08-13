using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesIntegrationApi.Abstractions;
using QuotesIntegrationApi.Data;
using QuotesIntegrationApi.Models;
using Serilog;
using Serilog.Context;

const string ServiceName = "QuotesIntegrationApi";

var builder = WebApplication.CreateBuilder(args);

// Log levels per category (Microsoft.AspNetCore at Warning, EF Core SQL at Debug only in
// Development) live in appsettings.json / appsettings.Development.json under "Serilog", not here.
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// One ActivitySource for spans we start ourselves (e.g. "validate-create-quote-request" below) —
// registered with .AddSource so the OTel SDK actually samples/exports activities it creates.
builder.Services.AddSingleton(new ActivitySource(ServiceName));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddSource(ServiceName)
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");
        }));

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

// Every log line written while handling a request — ours, EF Core's, ASP.NET Core's — picks up
// this TraceId via Enrich.FromLogContext(), so a single request's lines can be grepped together.
// Activity.Current here is the span AddAspNetCoreInstrumentation() already started for this
// request, so this is the *same* TraceId the OTel exporter sends to Jaeger — not a second,
// unrelated identifier. ctx.TraceIdentifier is only a fallback for the (unreachable in this app)
// case of no tracing being configured at all.
app.Use((HttpContext ctx, RequestDelegate next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
    using (LogContext.PushProperty("TraceId", traceId))
    {
        return next(ctx);
    }
});

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Day 4 Piece 4: OpenTelemetry tracing.",
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
    ClaimsPrincipal caller,
    QuotesDbContext db,
    IClock clock,
    ILogger<Program> logger,
    ActivitySource activitySource,
    CancellationToken cancellationToken) =>
{
    var userId = caller.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? caller.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    logger.LogInformation("Received create-quote request from user {UserId} for author {Author}", userId, request.Author);

    // Validation itself is plain in-process logic — no EF, no HTTP call — so none of the automatic
    // instrumentations produce a span for it. Nested under the AddAspNetCoreInstrumentation()
    // request span automatically, since it's started while that span is Activity.Current.
    var errors = new Dictionary<string, string[]>();

    using (var activity = activitySource.StartActivity("validate-create-quote-request"))
    {
        activity?.SetTag("user.id", userId);

        if (string.IsNullOrWhiteSpace(request.Author))
            errors["author"] = ["Author is required."];
        else if (request.Author.Length > 100)
            errors["author"] = ["Author must be 100 characters or fewer."];

        if (string.IsNullOrWhiteSpace(request.Text))
            errors["text"] = ["Text is required."];
        else if (request.Text.Length > 1000)
            errors["text"] = ["Text must be 1000 characters or fewer."];

        activity?.SetTag("validation.error_count", errors.Count);
        if (errors.Count > 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Validation failed");
        }
    }

    if (errors.Count > 0)
    {
        logger.LogInformation(
            "Rejected create-quote request from user {UserId}: {ValidationErrorCount} validation errors",
            userId, errors.Count);
        return Results.ValidationProblem(errors);
    }

    var quote = new QuotesIntegrationApi.Models.Quote
    {
        Author = request.Author.Trim(),
        Text = request.Text.Trim(),
        CreatedAt = clock.UtcNow
    };

    db.Quotes.Add(quote);
    await db.SaveChangesAsync(cancellationToken);

    logger.LogInformation("Created quote {QuoteId} for user {UserId}", quote.Id, userId);

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
