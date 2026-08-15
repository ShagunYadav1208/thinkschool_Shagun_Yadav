using Microsoft.Extensions.Logging;
using OrderApi.DTOs;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services;

namespace OrderApi.Tests.Unit;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_WhenRequestHasNoItems_ThrowsValidationException()
    {
        var repository = new FakeOrderRepository();
        var logger = new TestLogger<OrderService>();
        var service = new OrderService(repository, logger);

        var request = new CreateOrderRequest
        {
            CustomerName = "Shagun Yadav",
            CustomerEmail = "shagun@example.com",
            Items = []
        };

        var exception = await Assert.ThrowsAsync<OrderValidationException>(
            () => service.CreateOrderAsync(
                request,
                CancellationToken.None));

        Assert.Equal(
            "Order must contain at least one item.",
            exception.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_WhenStockIsInsufficient_ThrowsValidationException()
    {
        var repository = new FakeOrderRepository
        {
            Product = new Product
            {
                Id = 1,
                Name = "Laptop",
                Price = 1000m,
                Stock = 2,
                IsActive = true
            }
        };

        var logger = new TestLogger<OrderService>();
        var service = new OrderService(repository, logger);

        var request = new CreateOrderRequest
        {
            CustomerName = "Shagun Yadav",
            CustomerEmail = "stock@example.com",
            Items =
            [
                new CreateOrderItemRequest
                {
                    ProductId = 1,
                    Quantity = 5
                }
            ]
        };

        var exception = await Assert.ThrowsAsync<OrderValidationException>(
            () => service.CreateOrderAsync(
                request,
                CancellationToken.None));

        Assert.Equal(
            "Not enough stock for product 1.",
            exception.Message);
    }

    [Fact]
    public async Task CreateOrderAsync_WithValidRequest_CalculatesTotalAndCreatesOrder()
    {
        var repository = new FakeOrderRepository
        {
            Product = new Product
            {
                Id = 1,
                Name = "Laptop",
                Price = 1000m,
                Stock = 20,
                IsActive = true
            }
        };

        var logger = new TestLogger<OrderService>();
        var service = new OrderService(repository, logger);

        var request = new CreateOrderRequest
        {
            CustomerName = "Shagun Yadav",
            CustomerEmail = "valid@example.com",
            ShippingAddress = "Noida, Uttar Pradesh",
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

        var result = await service.CreateOrderAsync(
            request,
            CancellationToken.None);

        Assert.NotNull(repository.SavedOrder);

        Assert.Equal(
            "Shagun Yadav",
            result.CustomerName);

        Assert.Equal(
            "valid@example.com",
            result.CustomerEmail);

        Assert.Equal(
            "Authorized",
            result.PaymentStatus);

        Assert.Equal(
            1171m,
            result.Total);

        Assert.Equal(
            50m,
            result.ShippingFee);

        Assert.Single(result.Items);

        Assert.Equal(
            "Laptop",
            result.Items[0].ProductName);

        Assert.Equal(
            1,
            result.Items[0].Quantity);

        Assert.Equal(
            1000m,
            result.Items[0].Total);

        Assert.Equal(
            19,
            repository.Product!.Stock);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public Customer? Customer { get; set; }

        public Product? Product { get; set; }

        public Order? SavedOrder { get; private set; }

        public Task<Customer?> GetCustomerByEmailAsync(
            string email,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Customer);
        }

        public Task<Product?> GetProductByIdAsync(
            int productId,
            CancellationToken cancellationToken)
        {
            if (Product?.Id == productId)
            {
                return Task.FromResult<Product?>(Product);
            }

            return Task.FromResult<Product?>(null);
        }

        public Task<bool> HasRecentOrderAsync(
            string customerEmail,
            TimeSpan window,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<int> GetCustomerOrderCountAsync(
            int customerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task AddCustomerAsync(
            Customer customer,
            CancellationToken cancellationToken)
        {
            Customer = customer;

            return Task.CompletedTask;
        }

        public Task AddOrderAsync(
            Order order,
            CancellationToken cancellationToken)
        {
            order.Id = 1;

            SavedOrder = order;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<Order?> GetOrderByIdAsync(
            int orderId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(SavedOrder);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(
            TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(
            LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}