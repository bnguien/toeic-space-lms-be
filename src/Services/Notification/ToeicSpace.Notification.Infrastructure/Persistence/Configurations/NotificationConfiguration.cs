using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationEntity = ToeicSpace.Notification.Domain.Entities.Notification;

namespace ToeicSpace.Notification.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(80).IsRequired();
        builder.Property(x => x.NavigationPath).HasMaxLength(512);
        builder.Property(x => x.CreatedAt).HasPrecision(6);
        builder.Property(x => x.ReadAt).HasPrecision(6);

        builder.HasIndex(x => new { x.Source, x.SourceEventId, x.RecipientId, x.Type }).IsUnique();
        builder.HasIndex(x => new { x.RecipientId, x.CreatedAt, x.Id }).IsDescending(false, true, true);
        builder.HasIndex(x => new { x.RecipientId, x.ReadAt, x.CreatedAt, x.Id })
            .IsDescending(false, false, true, true);
    }
}
