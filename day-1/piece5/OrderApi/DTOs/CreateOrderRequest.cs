namespace OrderApi.DTOs;

public class CreateOrderRequest
{
    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    public string? ShippingAddress { get; set; }

    public string? CouponCode { get; set; }

    public string? PaymentMethod { get; set; }

    public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public class CreateOrderItemRequest
{
    public int ProductId { get; set; }

    public int Quantity { get; set; }
}