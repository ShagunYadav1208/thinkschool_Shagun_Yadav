using CollectionApi.Data;
using CollectionApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Repositories;

public sealed class CollectionRepository(CollectionsDbContext db) : ICollectionRepository
{
    public async Task<IReadOnlyList<CollectionItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await db.CollectionItems
            .AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CollectionItem> AddAsync(CollectionItem item, CancellationToken cancellationToken)
    {
        await db.CollectionItems.AddAsync(item, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }
}
