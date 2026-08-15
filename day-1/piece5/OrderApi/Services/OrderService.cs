using OrderApi.DTOs;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services.Discounts;

namespace OrderApi.Services;

public class OrderService(
    IOrderRepository repository,
    IEnumerable<IDiscountRule> discountRules,
    ILogger<OrderService> logger) : IOrderService
{
    private const decimal TaxRate = 0.18m;
    private const decimal BulkDiscountRate = 0.10m;
    private const decimal CompanyDiscountRate = 0.05m;
    private const decimal LargeOrderShippingThreshold = 3000m;
    private const decimal FreeShippingFee = 0m;
    private const decimal StandardShippingFee = 50m;

    public async Task<OrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        if (await repository.HasRecentOrderAsync(
                request.CustomerEmail,
                TimeSpan.FromMinutes(5),
                cancellationToken))
        {
            throw new OrderValidationException(
                "A recent order already exists.");
        }

        var customer = await repository.GetCustomerByEmailAsync(
            request.CustomerEmail,
            cancellationToken);

        if (customer is null)
        {
            customer = new Customer
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail,
                CreatedAt = DateTime.UtcNow
            };

            await repository.AddCustomerAsync(
                customer,
                cancellationToken);
        }

        var order = new Order
        {
            Customer = customer,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            ShippingAddress = request.ShippingAddress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Status = "Pending",
            PaymentStatus = GetPaymentStatus(request.PaymentMethod)
        };

        decimal subtotal = 0m;

        foreach (var itemRequest in request.Items)
        {
            ValidateItem(itemRequest);

            var product = await repository.GetProductByIdAsync(
                itemRequest.ProductId,
                cancellationToken);

            if (product is null)
            {
                throw new OrderValidationException(
                    $"Product {itemRequest.ProductId} was not found.");
            }

            if (!product.IsActive)
            {
                throw new OrderValidationException(
                    $"Product {product.Id} is not available.");
            }

            if (product.Stock < itemRequest.Quantity)
            {
                throw new OrderValidationException(
                    $"Not enough stock for product {product.Id}.");
            }

            var unitPrice = CalculateUnitPrice(
                product,
                itemRequest.Quantity,
                request.CustomerEmail);

            var lineTotal = unitPrice * itemRequest.Quantity;

            order.Items.Add(new OrderItem
            {
                Product = product,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = itemRequest.Quantity,
                UnitPrice = unitPrice,
                Total = lineTotal
            });

            product.Stock -= itemRequest.Quantity;

            subtotal += lineTotal;
        }

        var discount = CalculateDiscount(
            subtotal,
            request.CouponCode);

        var taxableAmount = Math.Max(
            0m,
            subtotal - discount);

        var tax = taxableAmount * TaxRate;

        order.ShippingFee =
            subtotal >= LargeOrderShippingThreshold
                ? FreeShippingFee
                : StandardShippingFee;

        order.Total =
            taxableAmount +
            tax +
            order.ShippingFee;

        if (order.Total <= 0)
        {
            throw new OrderValidationException(
                "Order total must be greater than zero.");
        }

        order.Status = DetermineOrderStatus(
            order.Total,
            order.Items.Count);

        await repository.AddOrderAsync(
            order,
            cancellationToken);

        await repository.SaveChangesAsync(
            cancellationToken);

        var savedOrder = await repository.GetOrderByIdAsync(
            order.Id,
            cancellationToken);

        if (savedOrder is null)
        {
            throw new InvalidOperationException(
                "The order could not be retrieved after creation.");
        }

        logger.LogInformation(
            "Created order {OrderId} for {CustomerEmail}",
            savedOrder.Id,
            savedOrder.CustomerEmail);

        return MapToResponse(savedOrder);
    }

    public async Task<OrderResponse?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await repository.GetOrderByIdAsync(
            orderId,
            cancellationToken);

        return order is null
            ? null
            : MapToResponse(order);
    }

    private static void ValidateRequest(CreateOrderRequest request)
    {
        if (request is null)
        {
            throw new OrderValidationException(
                "Request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            throw new OrderValidationException(
                "Customer name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail) ||
            !request.CustomerEmail.Contains('@'))
        {
            throw new OrderValidationException(
                "A valid customer email is required.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new OrderValidationException(
                "Order must contain at least one item.");
        }

        if (request.Items.Count > 50)
        {
            throw new OrderValidationException(
                "An order cannot contain more than 50 items.");
        }

        if (request.ShippingAddress is not null &&
            request.ShippingAddress.Length < 10)
        {
            throw new OrderValidationException(
                "Shipping address is too short.");
        }
    }

    private static void ValidateItem(
        CreateOrderItemRequest item)
    {
        if (item.Quantity <= 0)
        {
            throw new OrderValidationException(
                "Quantity must be greater than zero.");
        }

        if (item.Quantity > 100)
        {
            throw new OrderValidationException(
                "Quantity cannot be greater than 100.");
        }
    }

    private static decimal CalculateUnitPrice(
        Product product,
        int quantity,
        string email)
    {
        var price = product.Price;

        if (quantity >= 10)
        {
            price *= 1 - BulkDiscountRate;
        }

        if (email.EndsWith(
                "@company.com",
                StringComparison.OrdinalIgnoreCase))
        {
            price *= 1 - CompanyDiscountRate;
        }

        return price;
    }

    private decimal CalculateDiscount(
        decimal subtotal,
        string? couponCode)
    {
        var context = new DiscountContext(subtotal, couponCode);

        var discount = discountRules.Sum(
            rule => rule.CalculateDiscount(context));

        return Math.Min(
            discount,
            subtotal);
    }

    private static string GetPaymentStatus(
        string? paymentMethod)
    {
        return paymentMethod switch
        {
            "Card" => "Authorized",
            "UPI" => "Authorized",
            "Cash" => "Pending",
            _ => "Pending"
        };
    }

    private static string DetermineOrderStatus(
        decimal total,
        int itemCount)
    {
        if (total > 50_000m)
        {
            return "FraudReview";
        }

        if (total > 10_000m)
        {
            return "ManualApproval";
        }

        if (total > 5_000m)
        {
            return "RequiresReview";
        }

        if (total > 2_000m)
        {
            return "Priority";
        }

        return itemCount > 5
            ? "LargeOrder"
            : "Pending";
    }

    private static OrderResponse MapToResponse(
        Order order)
    {
        return new OrderResponse
        {
            Id = order.Id,
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            Total = order.Total,
            ShippingFee = order.ShippingFee,
            CreatedAt = order.CreatedAt,
            Items = order.Items
                .Select(item => new OrderItemResponse
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    Total = item.Total
                })
                .ToList()
        };
    }
}