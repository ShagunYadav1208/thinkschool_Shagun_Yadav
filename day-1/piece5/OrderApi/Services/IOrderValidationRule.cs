using OrderApi.DTOs;

namespace OrderApi.Services;

public interface IOrderValidationRule
{
    void Validate(CreateOrderRequest request);
}
