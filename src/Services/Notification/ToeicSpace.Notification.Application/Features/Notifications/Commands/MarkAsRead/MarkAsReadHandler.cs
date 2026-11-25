namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAsRead;

public sealed class MarkAsReadHandler(INotificationRepository repository, Services.NotificationDelivery delivery,
    TimeProvider clock) : IRequestHandler<MarkAsReadCommand, bool>
{
    public async Task<bool> Handle(MarkAsReadCommand request, CancellationToken cancellationToken)
    {
        var changed = await repository.MarkReadAsync(request.RecipientId, request.Id,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (changed == 0)
            return await repository.FindAsync(request.RecipientId, request.Id, cancellationToken) is not null;

        await delivery.RefreshUnreadCountAsync(request.RecipientId, cancellationToken);
        return true;
    }
}
