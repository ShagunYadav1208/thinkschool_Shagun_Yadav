using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Repositories;
using OrderApi.Services;
using OrderApi.Services.Discounts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=orders.db"));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

builder.Services.AddSingleton<IDiscountRule, BulkOrderDiscountRule>();
builder.Services.AddSingleton<IDiscountRule, Welcome10CouponDiscountRule>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

public partial class Program;