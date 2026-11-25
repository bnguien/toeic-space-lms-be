using MassTransit;
using MediatR;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

namespace ToeicSpace.Notification.Infrastructure.Consumers;

public sealed class ActiveAccountCreatedConsumer(ISender sender) : IConsumer<ActiveAccountCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<ActiveAccountCreatedIntegrationEvent> context)
    {
        var message = context.Message;
        return sender.Send(new CreateNotificationCommand([message.UserId], "Tài khoản TOEICSpace đã sẵn sàng",
            "Bạn đã có thể đăng nhập và sử dụng TOEICSpace.",
            "ActiveAccountCreated", "Identity", message.Id, message.UserId, null), context.CancellationToken);
    }
}

public sealed class ActiveAccountCreatedConsumerDefinition : ConsumerDefinition<ActiveAccountCreatedConsumer>
{
    public ActiveAccountCreatedConsumerDefinition() => EndpointName = "notification-active-account-created";

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,
        IConsumerConfigurator<ActiveAccountCreatedConsumer> consumer, IRegistrationContext context) =>
        NotificationConsumerRetry.Configure(endpoint);
}
