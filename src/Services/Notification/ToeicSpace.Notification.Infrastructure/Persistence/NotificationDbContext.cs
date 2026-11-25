using Microsoft.EntityFrameworkCore;
using NotificationEntity = ToeicSpace.Notification.Domain.Entities.Notification;

namespace ToeicSpace.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4").UseCollation("utf8mb4_0900_ai_ci");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }

    internal static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseMySql(connection, new MySqlServerVersion(new Version(8, 4, 0)));
}
