using MassTransit;
using MediatR;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

namespace ToeicSpace.Notification.Infrastructure.Consumers;

public sealed class ExamAttemptSubmittedConsumer(ISender sender) : IConsumer<ExamAttemptSubmittedIntegrationEvent>
{
    public Task Consume(ConsumeContext<ExamAttemptSubmittedIntegrationEvent> context)
    {
        var message = context.Message;
        return sender.Send(new CreateNotificationCommand([message.UserId], "Đã có kết quả bài thi",
            "Bài thi của bạn đã được chấm điểm. Mở kết quả để xem chi tiết.",
            "ExamAttemptScored", "Assessment", message.Id, message.AttemptId, null), context.CancellationToken);
    }
}

public sealed class ExamAttemptSubmittedConsumerDefinition : ConsumerDefinition<ExamAttemptSubmittedConsumer>
{
    public ExamAttemptSubmittedConsumerDefinition() => EndpointName = "notification-exam-attempt-submitted";

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,
        IConsumerConfigurator<ExamAttemptSubmittedConsumer> consumer, IRegistrationContext context) =>
        NotificationConsumerRetry.Configure(endpoint);
}
