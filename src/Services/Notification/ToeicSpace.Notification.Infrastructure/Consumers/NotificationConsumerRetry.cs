using MassTransit;

namespace ToeicSpace.Notification.Infrastructure.Consumers;

internal static class NotificationConsumerRetry
{
    public static void Configure(IReceiveEndpointConfigurator endpoint)
    {
        endpoint.UseMessageRetry(retry =>
        {
            retry.Ignore<FluentValidation.ValidationException>();
            retry.Interval(5, TimeSpan.FromSeconds(1));
        });
    }
}
