using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesIntegrationApi.Abstractions;
using QuotesIntegrationApi.Configuration;
using QuotesIntegrationApi.Data;
using QuotesIntegrationApi.Models;
using QuotesIntegrationApi.Services;
using Serilog;
using Serilog.Context;

const string ServiceName = "QuotesIntegrationApi";

var builder = WebApplication.CreateBuilder(args);

// Secrets never live in appsettings.json. In Azure App Service, KeyVault:Uri is set as an app
// setting and DefaultAzureCredential resolves to the App Service's managed identity — no client
// secret anywhere, ever. Locally, with no KeyVault:Uri configured, this is a no-op and the app
// falls back to appsettings.json / user-secrets / environment variables as usual.
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// Log levels per category (Microsoft.AspNetCore at Warning, EF Core SQL at Debug only in
// Development) live in appsettings.json / appsettings.Development.json under "Serilog", not here.
builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// One ActivitySource for spans we start ourselves (e.g. "validate-create-quote-request" below) —
// registered with .AddSource so the OTel SDK actually samples/exports activities it creates.
builder.Services.AddSingleton(new ActivitySource(ServiceName));

var tracing = builder.Services.AddOpenTelemetry()
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

// The connection string above comes from Key Vault (via AddAzureKeyVault) once deployed — never
// hardcoded, never in a config file checked into source control. Locally, with nothing configured,
// this stays off and the local OTLP-to-Jaeger export above is the only exporter, unchanged from
// Piece 4. In Azure, UseAzureMonitor exports the same traces (plus logs and metrics) to
// Application Insights, on top of — not instead of — local export.
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    tracing.UseAzureMonitor(options => options.ConnectionString = appInsightsConnectionString);
}

// Config precedence (highest to lowest): environment variables, appsettings.{Environment}.json,
// appsettings.json. Jwt:Key specifically never lives in any of those checked-in files — locally
// it comes from `dotnet user-secrets set Jwt:Key "..."` (see README), in production from a Key
// Vault reference set as an app setting (an environment variable, so it still wins over any file).
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidation>();

builder.Services.AddOptions<QuoteValidationOptions>()
    .Bind(builder.Configuration.GetSection("QuoteValidation"));

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=quotes-integration.db"));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<ITokenService, TokenService>();

// JwtBearerOptionsSetup reads IOptions<JwtOptions> to build TokenValidationParameters, so there's
// nothing left to configure inline here.
builder.Services.ConfigureOptions<JwtBearerOptionsSetup>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

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
    message = "Day 4 Piece 6: configuration done right with IOptions.",
    endpoints = new[]
    {
        "POST /auth/token",
        "GET /api/quotes",
        "GET /api/quotes/{id}",
        "POST /api/quotes",
        "DELETE /api/quotes/{id}"
    }
}));

app.MapPost("/auth/token", (TokenRequest request, ITokenService tokenService) =>
{
    var token = tokenService.CreateToken(request.Subject);
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
    IOptionsSnapshot<QuoteValidationOptions> quoteValidationOptions,
    CancellationToken cancellationToken) =>
{
    var userId = caller.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? caller.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "unknown";

    logger.LogInformation("Received create-quote request from user {UserId} for author {Author}", userId, request.Author);

    // IOptionsSnapshot is scoped — resolved fresh per request — which is the right fit here:
    // unlike Jwt:Key, these limits are safe to change without a restart, and a snapshot means a
    // config reload takes effect on the very next request instead of needing IOptionsMonitor's
    // extra CurrentValue/OnChange machinery this handler has no use for.
    var limits = quoteValidationOptions.Value;

    // Validation itself is plain in-process logic — no EF, no HTTP call — so none of the automatic
    // instrumentations produce a span for it. Nested under the AddAspNetCoreInstrumentation()
    // request span automatically, since it's started while that span is Activity.Current.
    var errors = new Dictionary<string, string[]>();

    using (var activity = activitySource.StartActivity("validate-create-quote-request"))
    {
        activity?.SetTag("user.id", userId);

        if (string.IsNullOrWhiteSpace(request.Author))
            errors["author"] = ["Author is required."];
        else if (request.Author.Length > limits.MaxAuthorLength)
            errors["author"] = [$"Author must be {limits.MaxAuthorLength} characters or fewer."];

        if (string.IsNullOrWhiteSpace(request.Text))
            errors["text"] = ["Text is required."];
        else if (request.Text.Length > limits.MaxTextLength)
            errors["text"] = [$"Text must be {limits.MaxTextLength} characters or fewer."];

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

public partial class Program;
