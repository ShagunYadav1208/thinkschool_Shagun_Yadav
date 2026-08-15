using OrderApi.Models;

namespace OrderApi.Repositories;

public interface IOrderRepository
{
    Task<Customer?> GetCustomerByEmailAsync(
        string email,
        CancellationToken cancellationToken);

    Task<Product?> GetProductByIdAsync(
        int productId,
        CancellationToken cancellationToken);

    Task<bool> HasRecentOrderAsync(
        string customerEmail,
        TimeSpan window,
        CancellationToken cancellationToken);

    Task<int> GetCustomerOrderCountAsync(
        int customerId,
        CancellationToken cancellationToken);

    Task AddCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken);

    Task AddOrderAsync(
        Order order,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(
        CancellationToken cancellationToken);

    Task<Order?> GetOrderByIdAsync(
        int orderId,
        CancellationToken cancellationToken);
}