using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkFlow.Modules.Parking.Domain;

namespace ParkFlow.Modules.Parking.Infrastructure.Persistence;

internal sealed class ParkingSpotConfiguration : IEntityTypeConfiguration<ParkingSpot>
{
    public void Configure(EntityTypeBuilder<ParkingSpot> builder)
    {
        builder.ToTable("ParkingSpots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SpotNumber).HasMaxLength(20).IsRequired();
        builder.Property(s => s.SpotType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(s => new { s.FacilityId, s.Status });
        builder.Ignore(s => s.DomainEvents);
    }
}
