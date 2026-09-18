using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ParkFlow.Modules.Notification.Infrastructure.Persistence;

using Domain = ParkFlow.Modules.Notification.Domain.NotificationMessage;

internal sealed class NotificationMessageConfiguration : IEntityTypeConfiguration<Domain>
{
    public void Configure(EntityTypeBuilder<Domain> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(n => n.DomainEvents);
    }
}
