namespace OrderApi.DTOs;

public sealed class CreateOrderRequest
{
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public List<CreateOrderItemRequest> Items { get; init; } = [];
    public string? ShippingAddress { get; init; }
}

public sealed class CreateOrderItemRequest
{
    public int ProductId { get; init; }
    public int Quantity { get; init; }
}
