using OrderApi.DTOs;

namespace OrderApi.Services.Rules;

public sealed class RequestShapeRule : IOrderValidationRule
{
    public void Validate(CreateOrderRequest request)
    {
        if (request is null)
            throw new OrderValidationException("Request cannot be null.");
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            throw new OrderValidationException("Customer name is required.");
        if (string.IsNullOrWhiteSpace(request.CustomerEmail) || !request.CustomerEmail.Contains('@'))
            throw new OrderValidationException("A valid customer email is required.");
        if (request.Items is null || request.Items.Count == 0)
            throw new OrderValidationException("Order must contain at least one item.");
        if (request.Items.Count > 50)
            throw new OrderValidationException("An order cannot contain more than 50 items.");
    }
}
