using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Models;

namespace OrderApi.Repositories;

public class OrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Customer?> GetCustomerByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        return db.Customers
            .FirstOrDefaultAsync(
                c => c.Email == email,
                cancellationToken);
    }

    public Task<Product?> GetProductByIdAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        return db.Products
            .FirstOrDefaultAsync(
                p => p.Id == productId,
                cancellationToken);
    }

    public Task<bool> HasRecentOrderAsync(
        string customerEmail,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.Subtract(window);

        return db.Orders.AnyAsync(
            o => o.CustomerEmail == customerEmail &&
                 o.CreatedAt > cutoff,
            cancellationToken);
    }

    public Task<int> GetCustomerOrderCountAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        return db.Orders
            .CountAsync(
                o => o.CustomerId == customerId,
                cancellationToken);
    }

    public async Task AddCustomerAsync(
        Customer customer,
        CancellationToken cancellationToken)
    {
        await db.Customers.AddAsync(customer, cancellationToken);
    }

    public async Task AddOrderAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        await db.Orders.AddAsync(order, cancellationToken);
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }

    public Task<Order?> GetOrderByIdAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        return db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId,
                cancellationToken);
    }
}