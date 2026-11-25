using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ToeicSpace.Notification.Infrastructure.Persistence;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDatabase")
            ?? throw new InvalidOperationException("Set ConnectionStrings__NotificationDatabase for EF tooling.");
        var options = new DbContextOptionsBuilder<NotificationDbContext>();
        NotificationDbContext.Configure(options, connection);
        return new NotificationDbContext(options.Options);
    }
}
