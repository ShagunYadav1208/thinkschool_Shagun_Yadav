using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using QuotesLockedApi;

const string SmartBearerScheme = "SmartBearer";

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

var jwt = builder.Configuration.GetRequiredSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");
var entra = builder.Configuration.GetRequiredSection("EntraId").Get<EntraOptions>()
    ?? throw new InvalidOperationException("EntraId configuration is required.");

jwt.Validate();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
builder.Services.AddSingleton<IQuoteStore, InMemoryQuoteStore>();
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddSingleton<IAuthorizationHandler, DeleteOwnQuoteHandler>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = SmartBearerScheme;
        options.DefaultChallengeScheme = SmartBearerScheme;
    })
    .AddPolicyScheme(SmartBearerScheme, "Issuer based JWT selector", options =>
    {
        options.ForwardDefaultSelector = context =>
            SmartBearerRouting.SelectScheme(context.Request.Headers.Authorization.ToString());
    })
    .AddJwtBearer(SmartBearerRouting.InternalJwtScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };
    })
    .AddJwtBearer(SmartBearerRouting.EntraJwtScheme, options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{entra.TenantId}/v2.0";
        options.Audience = entra.Audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{entra.TenantId}/v2.0",
                $"https://sts.windows.net/{entra.TenantId}/"
            ],
            ValidateAudience = true,
            ValidAudiences = SmartBearerRouting.GetAllowedEntraAudiences(entra.Audience),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("can-edit-quotes", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", "quotes.write");
    })
    .AddPolicy("can-delete-own-quote", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new DeleteOwnQuoteRequirement());
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Day 4 Piece 2: Quotes API auth codebase driven to 80% coverage.",
    auth = new[] { SmartBearerRouting.InternalJwtScheme, SmartBearerRouting.EntraJwtScheme },
    policies = new[] { "can-edit-quotes", "can-delete-own-quote" }
}));

app.MapPost("/auth/login", (
    LoginRequest request,
    IUserStore users,
    IRefreshTokenService refreshTokens) =>
{
    var user = users.FindByEmail(request.Email);
    if (user is null || user.Password != request.Password)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(refreshTokens.Issue(user));
})
    .AllowAnonymous();

app.MapPost("/auth/refresh", (
    RefreshRequest request,
    IRefreshTokenService refreshTokens) =>
{
    var result = refreshTokens.Refresh(request.RefreshToken);
    return result.Tokens is null ? Results.Unauthorized() : Results.Ok(result.Tokens);
})
    .AllowAnonymous();

app.MapPost("/auth/internal-token", (
    InternalTokenRequest request,
    IUserStore users,
    ITokenService tokens) =>
{
    if (request.ClientSecret is not "local-dev-secret")
    {
        return Results.Unauthorized();
    }

    var user = users.FindById(request.Subject);
    if (user is null)
    {
        return Results.NotFound();
    }

    var accessToken = tokens.CreateAccessToken(
        user,
        request.Scopes ?? user.Scopes,
        TimeSpan.FromSeconds(request.ExpiresInSeconds ?? 900));

    return Results.Ok(new AccessTokenResponse(accessToken, 900));
})
    .AllowAnonymous();

app.MapGet("/quotes", (IQuoteStore quotes) => Results.Ok(quotes.All()));

app.MapPost("/quotes", (
    CreateQuoteRequest request,
    ClaimsPrincipal user,
    IQuoteStore quotes) =>
{
    var created = quotes.Create(request.Text, GetUserId(user));
    return Results.Ok(created);
})
    .RequireAuthorization("can-edit-quotes");

app.MapPut("/quotes/{quoteId:int}", (
    int quoteId,
    UpdateQuoteRequest request,
    IQuoteStore quotes) =>
{
    var quote = quotes.Update(quoteId, request.Text);
    return quote is null ? Results.NotFound() : Results.Ok(quote);
})
    .RequireAuthorization("can-edit-quotes");

app.MapDelete("/quotes/{quoteId:int}", (
    int quoteId,
    IQuoteStore quotes) =>
{
    return quotes.Delete(quoteId) ? Results.NoContent() : Results.NotFound();
})
    .RequireAuthorization("can-delete-own-quote");

app.Run();

static string GetUserId(ClaimsPrincipal user) =>
    user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? throw new InvalidOperationException("Authenticated user has no subject claim.");

public partial class Program;
