using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Day3Piece1.EntraAuth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

const string SmartBearerScheme = "SmartBearer";
const string InternalJwtScheme = "InternalJwt";
const string EntraJwtScheme = "EntraJwt";

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

var internalIssuer = builder.Configuration["Jwt:Issuer"] ?? "ThinkSchool.Internal";
var internalAudience = builder.Configuration["Jwt:Audience"] ?? "ThinkSchool.Internal.Callers";
var internalKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key configuration is required.");
var entraTenantId = builder.Configuration["EntraId:TenantId"]
    ?? throw new InvalidOperationException("EntraId:TenantId configuration is required.");
var entraAudience = builder.Configuration["EntraId:Audience"]
    ?? throw new InvalidOperationException("EntraId:Audience configuration is required.");
var entraAuthority = $"https://login.microsoftonline.com/{entraTenantId}/v2.0";

if (Encoding.UTF8.GetByteCount(internalKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes for HMAC signing.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = SmartBearerScheme;
        options.DefaultChallengeScheme = SmartBearerScheme;
    })
    .AddPolicyScheme(SmartBearerScheme, "Issuer based JWT selector", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var token = EntraTokenRouting.GetBearerToken(context.Request.Headers.Authorization);
            var issuer = EntraTokenRouting.ReadIssuer(token);

            return EntraTokenRouting.IsEntraIssuer(issuer) ? EntraJwtScheme : InternalJwtScheme;
        };
    })
    .AddJwtBearer(InternalJwtScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(internalKey)),
            ValidateIssuer = true,
            ValidIssuer = internalIssuer,
            ValidateAudience = true,
            ValidAudience = internalAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    })
    .AddJwtBearer(EntraJwtScheme, options =>
    {
        options.Authority = entraAuthority;
        options.Audience = entraAudience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers =
            [
                $"https://login.microsoftonline.com/{entraTenantId}/v2.0",
                $"https://sts.windows.net/{entraTenantId}/"
            ],
            ValidateAudience = true,
            ValidAudiences = EntraTokenRouting.GetAllowedEntraAudiences(entraAudience),
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("InternalOnly", policy =>
    {
        policy.AddAuthenticationSchemes(InternalJwtScheme);
        policy.RequireAuthenticatedUser();
    })
    .AddPolicy("SpaUsers", policy =>
    {
        policy.AddAuthenticationSchemes(EntraJwtScheme);
        policy.RequireAuthenticatedUser();
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Day 3 Piece 1: internal JWT and Entra ID JWT are both wired.",
    endpoints = new[]
    {
        "POST /auth/internal-token",
        "GET /api/me",
        "GET /api/internal-report",
        "GET /api/spa-profile"
    }
}));

app.MapPost("/auth/internal-token", (TokenRequest request) =>
{
    if (request.ClientId is not "internal-worker" || request.ClientSecret is not "local-dev-secret")
    {
        return Results.Unauthorized();
    }

    var token = CreateInternalToken(request.ClientId, internalIssuer, internalAudience, internalKey);
    return Results.Ok(new
    {
        access_token = token,
        token_type = "Bearer",
        expires_in = 900,
        example = "curl -H \"Authorization: Bearer <access_token>\" https://localhost:5001/api/internal-report"
    });
});

app.MapGet("/api/me", (ClaimsPrincipal user) => Results.Ok(DescribeUser(user)))
    .RequireAuthorization();

app.MapGet("/api/internal-report", (ClaimsPrincipal user) => Results.Ok(new
{
    scheme = InternalJwtScheme,
    message = "Internal caller accepted.",
    caller = DescribeUser(user)
}))
    .RequireAuthorization("InternalOnly");

app.MapGet("/api/spa-profile", (ClaimsPrincipal user) => Results.Ok(new
{
    scheme = EntraJwtScheme,
    message = "Entra ID access token accepted.",
    caller = DescribeUser(user)
}))
    .RequireAuthorization("SpaUsers");

app.Run();

static string CreateInternalToken(string subject, string issuer, string audience, string key)
{
    var credentials = new SigningCredentials(
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, subject),
        new Claim(ClaimTypes.Name, subject),
        new Claim(ClaimTypes.Role, "InternalCaller")
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

static object DescribeUser(ClaimsPrincipal user) => new
{
    name = user.Identity?.Name,
    issuer = user.FindFirst("iss")?.Value,
    audience = user.FindFirst("aud")?.Value,
    subject = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value,
    claims = user.Claims.Select(claim => new { claim.Type, claim.Value }).ToArray()
};

public sealed record TokenRequest(string ClientId, string ClientSecret);

public partial class Program;
