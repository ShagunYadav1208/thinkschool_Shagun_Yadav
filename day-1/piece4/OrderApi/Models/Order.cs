namespace OrderApi.Models;

public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public string CustomerEmail { get; set; } = string.Empty;

    public string? ShippingAddress { get; set; }

    public string Status { get; set; } = "Pending";

    public string PaymentStatus { get; set; } = "Pending";

    public string? Notes { get; set; }

    public decimal Total { get; set; }

    public decimal ShippingFee { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}