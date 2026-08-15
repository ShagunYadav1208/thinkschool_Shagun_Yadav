using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuotePolicyApi;

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "QuotePolicyApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "QuotePolicyApi.Client";
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key configuration is required.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IQuoteOwnershipService, InMemoryQuoteOwnershipService>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnQuoteAuthorizationHandler>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.Sub
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
        policy.AddRequirements(new OwnQuoteRequirement());
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    message = "Day 3 Piece 2: authorization policies and claims.",
    policies = new[] { "can-edit-quotes", "can-delete-own-quote" }
}));

app.MapPost("/auth/token", (TokenRequest request) =>
{
    var token = CreateToken(request.Subject, request.Scope, jwtIssuer, jwtAudience, jwtKey);
    return Results.Ok(new { access_token = token, token_type = "Bearer" });
});

app.MapPut("/quotes/{quoteId:int}", (int quoteId, UpdateQuoteRequest request, ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        quoteId,
        request.Text,
        editedBy = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    });
})
    .RequireAuthorization("can-edit-quotes");

app.MapDelete("/quotes/{quoteId:int}", (int quoteId, ClaimsPrincipal user) =>
{
    return Results.Ok(new
    {
        quoteId,
        deletedBy = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
    });
})
    .RequireAuthorization("can-delete-own-quote");

app.Run();

static string CreateToken(string subject, string? scope, string issuer, string audience, string key)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, subject),
        new(ClaimTypes.NameIdentifier, subject)
    };

    if (!string.IsNullOrWhiteSpace(scope))
    {
        claims.Add(new Claim("scope", scope));
    }

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        notBefore: DateTime.UtcNow,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256));

    return new JwtSecurityTokenHandler().WriteToken(token);
}

public sealed record TokenRequest(string Subject, string? Scope);

public sealed record UpdateQuoteRequest(string Text);

public partial class Program;
