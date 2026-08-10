using OrderApi.DTOs;

namespace OrderApi.Services;

public interface IOrderService
{
    Task<OrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken);

    Task<OrderResponse?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken);
}