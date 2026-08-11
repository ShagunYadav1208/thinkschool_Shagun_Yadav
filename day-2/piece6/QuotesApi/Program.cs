using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Extensions;
using QuotesApi.Models;
using QuotesApi.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwt = builder.Configuration.GetRequiredSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is required.");
jwt.Validate();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetRequiredSection(JwtOptions.SectionName));
builder.Services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Quotes") ?? "Data Source=quotes.db"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
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
        ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        var seed = builder.Configuration.GetRequiredSection("SeedUser").Get<SeedUserOptions>()!;
        db.Users.Add(new User { Email = seed.Email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(seed.Password) });
        await db.SaveChangesAsync();
    }
}

app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.Run();

public partial class Program;
