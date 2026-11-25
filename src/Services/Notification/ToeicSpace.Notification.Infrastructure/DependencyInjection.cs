using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToeicSpace.BuildingBlocks.Messaging.MassTransit;
using ToeicSpace.Notification.Application.Interfaces;
using ToeicSpace.Notification.Infrastructure.Consumers;
using ToeicSpace.Notification.Infrastructure.Persistence;
using ToeicSpace.Notification.Infrastructure.Persistence.Repositories;

namespace ToeicSpace.Notification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("NotificationDatabase");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("NotificationDatabase connection string is required.");

        services.AddDbContext<NotificationDbContext>(options => NotificationDbContext.Configure(options, connection));
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddMessagingBus(config, messaging =>
        {
            messaging.AddConsumer<ExamAttemptSubmittedConsumer>(typeof(ExamAttemptSubmittedConsumerDefinition));
            messaging.AddConsumer<LmsActivityConsumer>(typeof(LmsActivityConsumerDefinition));
            messaging.AddConsumer<ActiveAccountCreatedConsumer>(typeof(ActiveAccountCreatedConsumerDefinition));
        });
        return services;
    }
}
