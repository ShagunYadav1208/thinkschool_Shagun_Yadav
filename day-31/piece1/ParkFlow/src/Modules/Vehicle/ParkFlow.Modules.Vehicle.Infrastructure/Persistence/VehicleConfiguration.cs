using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkFlow.Modules.Vehicle.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Vehicle.Domain.Vehicle;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.LicensePlate).HasMaxLength(20).IsRequired();
        builder.Property(v => v.VehicleType).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(v => v.LicensePlate).IsUnique();
        builder.Ignore(v => v.DomainEvents);
    }
}
