using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ParkFlow.Modules.Parking.Domain;

namespace ParkFlow.Modules.Parking.Infrastructure.Persistence;

internal sealed class ParkingFacilityConfiguration : IEntityTypeConfiguration<ParkingFacility>
{
    public void Configure(EntityTypeBuilder<ParkingFacility> builder)
    {
        builder.ToTable("ParkingFacilities");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Address).HasMaxLength(400).IsRequired();
        builder.Ignore(f => f.DomainEvents);
    }
}
