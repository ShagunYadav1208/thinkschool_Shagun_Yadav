namespace OrderApi.Services.Discounts;

public record DiscountContext(
    decimal Subtotal,
    string? CouponCode);
