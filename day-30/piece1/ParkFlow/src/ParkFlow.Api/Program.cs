using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ParkFlow.Api.Security;
using ParkFlow.Modules.Notification.Application;
using ParkFlow.Modules.Notification.Infrastructure;
using ParkFlow.Modules.Parking.Application;
using ParkFlow.Modules.Parking.Infrastructure;
using ParkFlow.Modules.Payment.Application;
using ParkFlow.Modules.Payment.Infrastructure;
using ParkFlow.Modules.Reservation.Application;
using ParkFlow.Modules.Reservation.Infrastructure;
using ParkFlow.Modules.Vehicle.Application;
using ParkFlow.Modules.Vehicle.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Day 27 hardening: don't advertise the server stack, and cap request bodies so no endpoint can
// be used to exhaust memory with an oversized payload (see THREAT-MODEL.md, boundary A / DoS).
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 32 * 1024;
});

builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new Asp.Versioning.UrlSegmentApiVersionReader();
})
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
});

// Interim auth (see THREAT-MODEL.md section 5 for why API key, not Entra ID, this pass): every
// endpoint requires a valid X-Api-Key unless explicitly marked [AllowAnonymous]. Default/fallback
// scheme stays ApiKey — the JWT bearer scheme below is added alongside it (ADR-001,
// day-28/piece1), not instead of it, and only required on the three routes that need to know
// *which user* is calling.
builder.Services
    .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationDefaults.Scheme, _ => { })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        JwtBearerSetup.Configure(options, builder.Configuration[JwtBearerSetup.SigningKeyConfigPath] ?? string.Empty));

// Named, not default, options: AddScheme registers/resolves TOptions under the scheme name
// ("ApiKey"), so an unnamed Configure<T>() here would silently configure options nobody reads.
builder.Services.Configure<ApiKeyAuthenticationOptions>(ApiKeyAuthenticationDefaults.Scheme, options =>
{
    options.ApiKey = builder.Configuration["Security:ApiKey"] ?? string.Empty;
});

builder.Services.AddSingleton<IAuthorizationHandler, RequireApiKeyAndBearerHandler>();

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.Scheme)
        .RequireAuthenticatedUser()
        .Build())
    // ADR-001: the three reservation-mutation routes need both schemes — see
    // RequireApiKeyAndBearerHandler for why the AuthenticationSchemes attribute alone isn't enough.
    .AddPolicy(ReservationMutationPolicy.Name, policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthenticationDefaults.Scheme, JwtBearerDefaults.AuthenticationScheme)
        .AddRequirements(new RequireApiKeyAndBearerRequirement()));

// Fixed-window limiter keyed by API key (falling back to remote IP for unauthenticated callers)
// so one client flooding the API can't starve every other client — see THREAT-MODEL.md, DoS row.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

builder.Services.AddProblemDetails();

// Each module wires its own Application use cases and Infrastructure (persistence, outbox,
// caching, senders) independently — the composition root only assembles them, it never contains
// business logic itself. Day 22 uses EF Core's InMemory provider so the scaffold runs without a
// real SQL Server instance; swapping to a real provider is a one-line change per module here.
// Day 27 puts the *target* (SQL) data tier behind a private endpoint — see ../../infra/.
builder.Services
    .AddParkingApplication()
    .AddParkingInfrastructure(db => db.UseInMemoryDatabase("ParkFlow.Parking"))
    .AddReservationApplication()
    .AddReservationInfrastructure(db => db.UseInMemoryDatabase("ParkFlow.Reservation"))
    .AddVehicleApplication()
    .AddVehicleInfrastructure(db => db.UseInMemoryDatabase("ParkFlow.Vehicle"))
    .AddPaymentApplication()
    .AddPaymentInfrastructure(db => db.UseInMemoryDatabase("ParkFlow.Payment"))
    .AddNotificationApplication()
    .AddNotificationInfrastructure(db => db.UseInMemoryDatabase("ParkFlow.Notification"));

var app = builder.Build();

// Fail closed: a production-shaped environment with no key configured would otherwise start up
// with every endpoint effectively unauthenticated (HandleAuthenticateAsync always fails, but only
// because there'd be nothing correct to send — better to refuse to start at all).
if (!app.Environment.IsDevelopment() && string.IsNullOrEmpty(app.Configuration["Security:ApiKey"]))
{
    throw new InvalidOperationException(
        "Security:ApiKey must be configured outside Development — refusing to start with authentication effectively disabled.");
}

// Same posture for the JWT signing key (ADR-001): the bearer scheme is wired up in every
// environment (Build Day 32 swaps the dev issuer for a real IdP via config, not code), so an empty
// key outside Development would otherwise defer this failure to the first request instead of
// startup.
if (!app.Environment.IsDevelopment() && string.IsNullOrEmpty(app.Configuration[JwtBearerSetup.SigningKeyConfigPath]))
{
    throw new InvalidOperationException(
        $"{JwtBearerSetup.SigningKeyConfigPath} must be configured outside Development — refusing to start with the bearer scheme effectively unusable.");
}

// Centralized error handling: no unhandled exception ever reaches the client as a stack trace.
// A domain guard clause (ArgumentException/InvalidOperationException from the aggregates — see
// the Day 22 README's Reservation state machine) is a client input problem, so it maps to 400;
// anything else is genuinely unexpected and maps to a bare 500 with no exception detail.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var isClientFault = exception is ArgumentException or InvalidOperationException;

        context.Response.StatusCode = isClientFault
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = isClientFault ? "Invalid request." : "An unexpected error occurred.",
            status = context.Response.StatusCode,
        });
    });
});

app.UseParkFlowSecurityHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

// Development-only token issuance (ADR-001): mints a JWT for one of the seeded demo users so the
// composite policy above has something to test against. Not mapped at all outside Development —
// the same fail-closed posture as the ApiKey guard above, but by omission rather than a runtime
// check, since there is no legitimate way to call this safely outside a local dev box.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/v1/dev/token", (DevTokenRequest request) =>
    {
        if (!DemoUsers.ByName.TryGetValue(request.User, out var userId))
        {
            return Results.BadRequest(new
            {
                error = $"Unknown demo user '{request.User}'. Known users: {string.Join(", ", DemoUsers.ByName.Keys)}.",
            });
        }

        var signingKey = app.Configuration[JwtBearerSetup.SigningKeyConfigPath] ?? string.Empty;
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var token = new JwtSecurityToken(
            issuer: JwtBearerSetup.Issuer,
            audience: JwtBearerSetup.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            expires: expiresAt,
            signingCredentials: credentials);

        return Results.Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            userId,
            expiresAt,
        });
    }).AllowAnonymous();
}

// The OpenAPI document itself describes the API's shape and is only anonymous in Development —
// outside it, describing every route/DTO to an unauthenticated caller is its own small
// information disclosure, so it requires the same API key as everything else.
var openApiEndpoint = app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    openApiEndpoint.AllowAnonymous();
}

app.Run();

public partial class Program;
