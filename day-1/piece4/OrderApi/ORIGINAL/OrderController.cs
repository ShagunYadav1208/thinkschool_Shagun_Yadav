using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
private readonly AppDbContext _db;
private readonly ILogger<OrderController> _logger;

public OrderController(AppDbContext db, ILogger<OrderController> logger)
{
    _db = db;
    _logger = logger;
}

[HttpPost]
public async Task<object> CreateOrder(CreateOrderRequest request)
{
    try
    {
        if (request == null)
        {
            return new
            {
                success = false,
                message = "Request cannot be null"
            };
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return new
            {
                success = false,
                message = "Order must contain at least one item"
            };
        }

        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return new
            {
                success = false,
                message = "Customer name is required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return new
            {
                success = false,
                message = "Customer email is required"
            };
        }

        if (!request.CustomerEmail.Contains("@"))
        {
            return new
            {
                success = false,
                message = "Invalid customer email"
            };
        }

        if (request.Items.Count > 50)
        {
            return new
            {
                success = false,
                message = "Too many items"
            };
        }

        var customer = _db.Customers
            .FirstOrDefault(x => x.Email == request.CustomerEmail);

        if (customer == null)
        {
            customer = new Customer
            {
                Name = request.CustomerName,
                Email = request.CustomerEmail,
                CreatedAt = DateTime.UtcNow
            };

            _db.Customers.Add(customer);

            try
            {
                _db.SaveChanges();
            }
            catch
            {
            }
        }

        var order = new Order
        {
            CustomerId = customer.Id,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            CreatedAt = DateTime.UtcNow,
            Status = "Pending",
            Total = 0
        };

        _db.Orders.Add(order);

        decimal subtotal = 0;
        decimal tax = 0;
        decimal discount = 0;

        for (int i = 0; i <= request.Items.Count - 1; i++)
        {
            var item = request.Items[i];

            if (item.Quantity <= 0)
            {
                return new
                {
                    success = false,
                    message = "Quantity must be greater than zero",
                    item = i
                };
            }

            if (item.Quantity > 100)
            {
                return new
                {
                    success = false,
                    message = "Quantity cannot be greater than 100",
                    item = i
                };
            }

            var product = _db.Products
                .FirstOrDefault(x => x.Id == item.ProductId);

            if (product == null)
            {
                return new
                {
                    success = false,
                    message = "Product not found",
                    productId = item.ProductId
                };
            }

            if (!product.IsActive)
            {
                return new
                {
                    success = false,
                    message = "Product is not available",
                    productId = product.Id
                };
            }

            if (product.Stock < item.Quantity)
            {
                return new
                {
                    success = false,
                    message = "Not enough stock",
                    productId = product.Id,
                    available = product.Stock
                };
            }

            var price = product.Price;

            if (item.Quantity >= 10)
            {
                price = price * 0.90m;
            }

            if (request.CustomerEmail.EndsWith("@company.com"))
            {
                price = price * 0.95m;
            }

            var lineTotal = price * item.Quantity;

            var orderItem = new OrderItem
            {
                Order = order,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = price,
                Total = lineTotal
            };

            _db.OrderItems.Add(orderItem);

            product.Stock -= item.Quantity;

            subtotal += lineTotal;

            if (subtotal > 500)
            {
                discount = subtotal * 0.05m;
            }

            if (subtotal > 1000)
            {
                discount = subtotal * 0.10m;
            }

            tax = (subtotal - discount) * 0.18m;

            order.Total = subtotal - discount + tax;

            try
            {
                var previousOrder = _db.Orders
                    .Where(x => x.CustomerId == customer.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefault();

                if (previousOrder != null &&
                    previousOrder.Total > 1000)
                {
                    discount += 25;
                }
            }
            catch
            {
            }
        }

        if (subtotal > 2000)
        {
            order.Status = "Priority";
        }

        if (request.CouponCode != null)
        {
            var coupon = _db.Coupons
                .FirstOrDefault(x => x.Code == request.CouponCode);

            if (coupon != null)
            {
                if (coupon.IsActive &&
                    coupon.ExpiryDate > DateTime.UtcNow)
                {
                    if (coupon.MinimumAmount <= subtotal)
                    {
                        if (coupon.Type == "Percentage")
                        {
                            discount += subtotal *
                                (coupon.Value / 100);
                        }
                        else
                        {
                            discount += coupon.Value;
                        }

                        order.Total =
                            subtotal - discount +
                            ((subtotal - discount) * 0.18m);
                    }
                }
            }
        }

        if (request.ShippingAddress != null)
        {
            order.ShippingAddress = request.ShippingAddress;

            if (request.ShippingAddress.Length < 10)
            {
                return new
                {
                    success = false,
                    message = "Shipping address is too short"
                };
            }
        }

        if (order.Total < 0)
        {
            order.Total = 0;
        }

        if (order.Total >= 5000)
        {
            order.Status = "RequiresReview";
        }

        if (request.PaymentMethod == "Card")
        {
            order.PaymentStatus = "Authorized";
        }
        else if (request.PaymentMethod == "Cash")
        {
            order.PaymentStatus = "Pending";
        }
        else if (request.PaymentMethod == "UPI")
        {
            order.PaymentStatus = "Authorized";
        }
        else
        {
            order.PaymentStatus = "Pending";
        }

        try
        {
            var duplicate = _db.Orders
                .FirstOrDefault(x =>
                    x.CustomerEmail == request.CustomerEmail &&
                    x.CreatedAt > DateTime.UtcNow.AddMinutes(-5));

            if (duplicate != null)
            {
                return new
                {
                    success = false,
                    message = "A recent order already exists"
                };
            }
        }
        catch
        {
        }

        order.UpdatedAt = DateTime.UtcNow;

        if (order.Total > 10000)
        {
            order.Status = "ManualApproval";
        }

        if (request.Items.Count > 5)
        {
            order.Notes = "Large order";
        }

        if (request.CustomerName.Length > 100)
        {
            order.CustomerName =
                request.CustomerName.Substring(0, 100);
        }

        if (request.Items.Count > 0)
        {
            var firstItem = request.Items[0];

            var firstProduct = _db.Products
                .Find(firstItem.ProductId);

            if (firstProduct != null)
            {
                order.Notes =
                    "First product: " + firstProduct.Name;
            }
        }

        var existingOrders = _db.Orders
            .Where(x => x.CustomerId == customer.Id)
            .ToList();

        if (existingOrders.Count > 10)
        {
            order.Notes += " Returning customer";
        }

        if (request.CouponCode == "WELCOME10")
        {
            discount += 10;
            order.Total =
                subtotal - discount +
                ((subtotal - discount) * 0.18m);
        }

        if (order.Total > 3000)
        {
            order.ShippingFee = 0;
        }
        else
        {
            order.ShippingFee = 50;
        }

        order.Total += order.ShippingFee;

        if (order.Total > 50000)
        {
            order.Status = "FraudReview";
        }

        if (order.Total <= 0)
        {
            return new
            {
                success = false,
                message = "Order total must be greater than zero"
            };
        }

        try
        {
            _db.SaveChanges();
        }
        catch
        {
        }

        var savedOrder = _db.Orders
            .Include(x => x.Items)
            .FirstOrDefault(x => x.Id == order.Id);

        if (savedOrder == null)
        {
            return new
            {
                success = false,
                message = "Could not create order"
            };
        }

        var responseItems = new List<object>();

        for (int i = 0; i < savedOrder.Items.Count; i++)
        {
            var savedItem = savedOrder.Items[i];

            responseItems.Add(new
            {
                id = savedItem.Id,
                productId = savedItem.ProductId,
                productName = savedItem.ProductName,
                quantity = savedItem.Quantity,
                unitPrice = savedItem.UnitPrice,
                total = savedItem.Total
            });
        }

        var response = new
        {
            success = true,
            order = new
            {
                id = savedOrder.Id,
                customer = savedOrder.CustomerName,
                email = savedOrder.CustomerEmail,
                status = savedOrder.Status,
                paymentStatus = savedOrder.PaymentStatus,
                subtotal = subtotal,
                discount = discount,
                tax = tax,
                shipping = savedOrder.ShippingFee,
                total = savedOrder.Total,
                createdAt = savedOrder.CreatedAt,
                items = responseItems
            }
        };

        _logger.LogInformation(
            "Created order {OrderId} for {CustomerEmail}",
            savedOrder.Id,
            savedOrder.CustomerEmail);

        await Task.CompletedTask;

        return response;
    }
    catch (ArgumentException)
    {
    }
    catch
    {
    }
}

}