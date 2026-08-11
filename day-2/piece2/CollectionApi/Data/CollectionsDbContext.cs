using CollectionApi.Models;
using Microsoft.EntityFrameworkCore;

namespace CollectionApi.Data;

public class CollectionsDbContext(DbContextOptions<CollectionsDbContext> options) : DbContext(options)
{
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();
}
