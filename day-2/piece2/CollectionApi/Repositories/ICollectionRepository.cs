using CollectionApi.Models;

namespace CollectionApi.Repositories;

public interface ICollectionRepository
{
    Task<IReadOnlyList<CollectionItem>> GetAllAsync(CancellationToken cancellationToken);
    Task<CollectionItem> AddAsync(CollectionItem item, CancellationToken cancellationToken);
}
