using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ParkFlow.Api.Security;
using ParkFlow.Modules.Identity.Application;
using ParkFlow.Modules.Identity.Infrastructure;
using ParkFlow.Modules.Identity.Infrastructure.Persistence;
using ParkFlow.Modules.Notification.Application;
using ParkFlow.Modules.Notification.Infrastructure;
using ParkFlow.Modules.Notification.Infrastructure.Persistence;
using ParkFlow.Modules.Parking.Application;
using ParkFlow.Modules.Parking.Application.Abstractions;
using ParkFlow.Modules.Parking.Domain;
using ParkFlow.Modules.Parking.Infrastructure;
using ParkFlow.Modules.Parking.Infrastructure.Persistence;
using ParkFlow.Modules.Payment.Application;
using ParkFlow.Modules.Payment.Infrastructure;
using ParkFlow.Modules.Payment.Infrastructure.Persistence;
using ParkFlow.Modules.Reservation.Application;
using ParkFlow.Modules.Reservation.Infrastructure;
using ParkFlow.Modules.Reservation.Infrastructure.Persistence;
using ParkFlow.Modules.Vehicle.Application;
using ParkFlow.Modules.Vehicle.Infrastructure;
using ParkFlow.Modules.Vehicle.Infrastructure.Persistence;

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
builder.Services.AddSingleton<JwtTokenService>();

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
// Day 31 polish: PermitLimit/Window were a hardcoded 60/1min — pulled into config (same defaults,
// no behavior change for anyone not setting them) purely so a load-test/perf-benchmark environment
// can raise the limit without editing code. Production's actual default is unchanged.
var rateLimitPermitLimit = builder.Configuration.GetValue("Security:RateLimit:PermitLimit", 60);
var rateLimitWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("Security:RateLimit:WindowSeconds", 60));
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
            PermitLimit = rateLimitPermitLimit,
            Window = rateLimitWindow,
            QueueLimit = 0,
        });
    });
});

builder.Services.AddProblemDetails();

// Each module wires its own Application use cases and Infrastructure (persistence, outbox,
// caching, senders) independently — the composition root only assembles them, it never contains
// business logic itself. Day 32: switched from EF Core's InMemory provider to SQLite — one file
// per module (mirrors the old per-module InMemory database names) so each module's schema lives
// independently and Database.EnsureCreated() below can't collide across modules sharing one file.
// Day 27 puts the *target* (SQL) data tier behind a private endpoint — see ../../infra/.
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);

string SqliteConnectionString(string moduleName) =>
    $"Data Source={Path.Combine(dataDirectory, $"parkflow-{moduleName}.db")}";

builder.Services
    .AddIdentityApplication()
    .AddIdentityInfrastructure(db => db.UseSqlite(SqliteConnectionString("identity")))
    .AddParkingApplication()
    .AddParkingInfrastructure(db => db.UseSqlite(SqliteConnectionString("parking")))
    .AddReservationApplication()
    .AddReservationInfrastructure(db => db.UseSqlite(SqliteConnectionString("reservation")))
    .AddVehicleApplication()
    .AddVehicleInfrastructure(db => db.UseSqlite(SqliteConnectionString("vehicle")))
    .AddPaymentApplication()
    .AddPaymentInfrastructure(db => db.UseSqlite(SqliteConnectionString("payment")))
    .AddNotificationApplication()
    .AddNotificationInfrastructure(db => db.UseSqlite(SqliteConnectionString("notification")));

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

// Day 32: the frontend (wwwroot/) is served here, before the rate limiter and the fallback
// auth policy — a browser loading the page has no X-Api-Key to send, and static files carry no
// application data worth rate-limiting. The API routes below are unaffected: this only serves a
// request when a matching static file actually exists on disk, otherwise it falls through exactly
// as before.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

// Day 32: lets wwwroot/app.js bootstrap with whichever API key *this* environment is actually
// configured with, instead of a value hardcoded into the shipped JS that would only be correct for
// one deployment. This doesn't weaken the API key as a control — anyone who can reach this
// endpoint could equally have read the key straight out of app.js's source (same exposure either
// way) — it just means the same frontend files work unmodified in Development and in the live demo.
app.MapGet("/api/v1/client-config", () => Results.Ok(new { apiKey = app.Configuration["Security:ApiKey"] ?? string.Empty }))
    .AllowAnonymous();

// The OpenAPI document itself describes the API's shape and is only anonymous in Development —
// outside it, describing every route/DTO to an unauthenticated caller is its own small
// information disclosure, so it requires the same API key as everything else.
var openApiEndpoint = app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    openApiEndpoint.AllowAnonymous();
}

// Day 32: SQLite needs its schema created once per file (the InMemory provider never needed this
// — every run started with a fresh, already-"created" empty store). One file per module (see
// SqliteConnectionString above) means each EnsureCreated() call is independent and can't observe a
// sibling module's tables as "the database already exists" and skip creating its own.
using (var schemaScope = app.Services.CreateScope())
{
    var sp = schemaScope.ServiceProvider;
    sp.GetRequiredService<IdentityDbContext>().Database.EnsureCreated();
    sp.GetRequiredService<ParkingDbContext>().Database.EnsureCreated();
    sp.GetRequiredService<ReservationDbContext>().Database.EnsureCreated();
    sp.GetRequiredService<VehicleDbContext>().Database.EnsureCreated();
    sp.GetRequiredService<PaymentDbContext>().Database.EnsureCreated();
    sp.GetRequiredService<NotificationDbContext>().Database.EnsureCreated();
}

// Day 32: seed one facility with a handful of spots so the frontend has something to book against.
// SQLite now persists this across restarts (the old InMemory provider started empty on every run)
// — guarded on an empty check so it only ever seeds once per database file.
using (var seedScope = app.Services.CreateScope())
{
    var facilityRepository = seedScope.ServiceProvider.GetRequiredService<IParkingFacilityRepository>();
    var spotRepository = seedScope.ServiceProvider.GetRequiredService<IParkingSpotRepository>();
    var unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    if ((await facilityRepository.GetAllAsync()).Count == 0)
    {
        var facility = ParkingFacility.Create("Downtown Garage", "100 Market St", floorCount: 2);
        facilityRepository.Add(facility);

        var spotPlan = new[]
        {
            (Floor: 1, Number: "A1", Type: ParkingSpotType.Standard),
            (Floor: 1, Number: "A2", Type: ParkingSpotType.Standard),
            (Floor: 1, Number: "A3", Type: ParkingSpotType.Compact),
            (Floor: 1, Number: "A4", Type: ParkingSpotType.Accessible),
            (Floor: 1, Number: "A5", Type: ParkingSpotType.ElectricVehicle),
            (Floor: 2, Number: "B1", Type: ParkingSpotType.Standard),
            (Floor: 2, Number: "B2", Type: ParkingSpotType.Standard),
            (Floor: 2, Number: "B3", Type: ParkingSpotType.Compact),
            (Floor: 2, Number: "B4", Type: ParkingSpotType.Motorcycle),
        };

        foreach (var (floor, number, type) in spotPlan)
        {
            spotRepository.Add(ParkingSpot.Create(facility.Id, floor, number, type));
        }

        await unitOfWork.SaveChangesAsync();
    }
}

app.Run();

public partial class Program;
