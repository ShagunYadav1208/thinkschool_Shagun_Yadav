using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    public DbSet<Collection> Collections => Set<Collection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var collection = modelBuilder.Entity<Collection>();
        collection.HasKey(entity => entity.Id);
        collection.Property(entity => entity.Name).HasMaxLength(80).IsRequired();
        collection.Property(entity => entity.OwnerId).HasMaxLength(200).IsRequired();
        collection.OwnsMany(entity => entity.Items, item =>
        {
            item.ToTable("CollectionItems");
            item.WithOwner().HasForeignKey("CollectionId");
            item.Property(collectionItem => collectionItem.QuoteId)
                .ValueGeneratedNever()
                .IsRequired();
            item.Property(collectionItem => collectionItem.AddedAt).IsRequired();
            item.HasKey("CollectionId", nameof(CollectionItem.QuoteId));
        });
        collection.Navigation(entity => entity.Items)
            .HasField("items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
