using OrderApi.DTOs;

namespace OrderApi.Services;

public sealed class ShippingAddressRule : IOrderValidationRule
{
    public void Validate(CreateOrderRequest request)
    {
        if (request.ShippingAddress is not null && request.ShippingAddress.Length < 10)
            throw new OrderValidationException("Shipping address is too short.");
    }
}
