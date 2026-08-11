using QuotesApi.Models;

namespace QuotesApi.Repositories;

public interface ICollectionRepository
{
    Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task AddAsync(Collection collection, CancellationToken cancellationToken);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken);
    Task DeleteAsync(Collection collection, CancellationToken cancellationToken);
}
