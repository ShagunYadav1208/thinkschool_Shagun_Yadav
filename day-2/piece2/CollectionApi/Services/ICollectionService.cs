using CollectionApi.Models;

namespace CollectionApi.Services;

public interface ICollectionService
{
    Task<IReadOnlyList<CollectionItem>> GetAllAsync(CancellationToken cancellationToken);
    Task<CollectionItem> CreateAsync(CreateCollectionItemRequest request, CancellationToken cancellationToken);
}
