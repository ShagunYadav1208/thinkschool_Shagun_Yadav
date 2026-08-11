using OrderApi.DTOs;

namespace OrderApi.Services;

public sealed class OrderService(OrderValidationPipeline validation)
{
    public void Validate(CreateOrderRequest request) => validation.Validate(request);
}
