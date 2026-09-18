using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkFlow.Modules.Reservation.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Reservation.Domain.Reservation;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("Reservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Price).HasPrecision(10, 2);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // Rule 7: the idempotency key must be unique so a retried create request can never insert
        // a second reservation.
        builder.HasIndex(r => r.IdempotencyKey).IsUnique();

        // Rule 1 (partial): a real overlap guarantee still needs either a serializable transaction
        // or a database-level exclusion constraint on (ParkingSpotId, [StartTime, EndTime)) for
        // active statuses — this index only makes the overlap *query* efficient.
        builder.HasIndex(r => new { r.ParkingSpotId, r.StartTime, r.EndTime });

        builder.Ignore(r => r.DomainEvents);
    }
}
