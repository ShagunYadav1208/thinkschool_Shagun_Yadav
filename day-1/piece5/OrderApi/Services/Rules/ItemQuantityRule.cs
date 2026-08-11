using OrderApi.DTOs;

namespace OrderApi.Services.Rules;

public sealed class ItemQuantityRule : IOrderValidationRule
{
    public void Validate(CreateOrderRequest request)
    {
        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
                throw new OrderValidationException("Quantity must be greater than zero.");
            if (item.Quantity > 100)
                throw new OrderValidationException("Quantity cannot be greater than 100.");
        }
    }
}
