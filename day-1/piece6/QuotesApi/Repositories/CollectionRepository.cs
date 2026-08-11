using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public sealed class CollectionRepository(QuotesDbContext db) : ICollectionRepository
{
    public Task<Collection?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Collections.Include(collection => collection.Items)
            .SingleOrDefaultAsync(collection => collection.Id == id, cancellationToken);

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken)
    {
        await db.Collections.AddAsync(collection, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Collection collection, CancellationToken cancellationToken)
    {
        db.Collections.Update(collection);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Collection collection, CancellationToken cancellationToken)
    {
        db.Collections.Remove(collection);
        await db.SaveChangesAsync(cancellationToken);
    }
}
