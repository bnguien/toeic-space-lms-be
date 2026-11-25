namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAllAsRead;

public sealed class MarkAllAsReadHandler(INotificationRepository repository,
    Services.NotificationDelivery delivery, TimeProvider clock) : IRequestHandler<MarkAllAsReadCommand>
{
    public async Task Handle(MarkAllAsReadCommand request, CancellationToken cancellationToken)
    {
        var changed = await repository.MarkReadAsync(request.RecipientId, null,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (changed > 0)
            await delivery.RefreshUnreadCountAsync(request.RecipientId, cancellationToken);
    }
}
