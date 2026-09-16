using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkFlow.Modules.Payment.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Payment.Domain.Payment;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(10, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.ReservationId);
        builder.Ignore(p => p.DomainEvents);
    }
}
