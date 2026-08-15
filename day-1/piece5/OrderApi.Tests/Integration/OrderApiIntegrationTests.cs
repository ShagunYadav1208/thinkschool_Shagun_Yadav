using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OrderApi.DTOs;

namespace OrderApi.Tests.Integration;

public class OrderApiIntegrationTests
    : IClassFixture<OrderApiIntegrationTests.OrderApiFactory>
{
    private readonly OrderApiFactory _factory;

    public OrderApiIntegrationTests(OrderApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrder_WithValidRequest_ReturnsCreatedWithCalculatedTotal()
    {
        var client = _factory.CreateClient();

        var request = new CreateOrderRequest
        {
            CustomerName = "Integration Tester",
            CustomerEmail = "integration@example.com",
            PaymentMethod = "UPI",
            Items =
            [
                new CreateOrderItemRequest
                {
                    ProductId = 1,
                    Quantity = 1
                }
            ]
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var order = await response.Content
            .ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(order);
        Assert.Equal("Authorized", order!.PaymentStatus);
        Assert.Equal(1171m, order.Total);
        Assert.Single(order.Items);
        Assert.Equal("Laptop", order.Items[0].ProductName);
    }

    [Fact]
    public async Task CreateOrder_WithNoItems_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var request = new CreateOrderRequest
        {
            CustomerName = "Integration Tester",
            CustomerEmail = "no-items@example.com",
            Items = []
        };

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task GetOrder_ForUnknownId_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders/999999");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }

    public class OrderApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"orderapi-tests-{Guid.NewGuid()}.db");

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                $"Data Source={_dbPath}");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                SqliteConnection.ClearAllPools();

                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
        }
    }
}
