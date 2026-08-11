using CollectionApi.Models;
using CollectionApi.Repositories;

namespace CollectionApi.Services;

public sealed class CollectionService(ICollectionRepository repository) : ICollectionService
{
    public Task<IReadOnlyList<CollectionItem>> GetAllAsync(CancellationToken cancellationToken) =>
        repository.GetAllAsync(cancellationToken);

    public Task<CollectionItem> CreateAsync(
        CreateCollectionItemRequest request,
        CancellationToken cancellationToken) =>
        repository.AddAsync(
            new CollectionItem { Name = request.Name.Trim() },
            cancellationToken);
}
