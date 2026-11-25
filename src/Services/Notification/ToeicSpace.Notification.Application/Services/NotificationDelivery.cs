using Microsoft.Extensions.Logging;

namespace ToeicSpace.Notification.Application.Services;

public sealed class NotificationDelivery(INotificationRealtime realtime, INotificationRepository repository,
    ILogger<NotificationDelivery> logger)
{
    public Task CreatedAsync(Guid recipientId, NotificationDto notification, CancellationToken ct) =>
        DeliverAsync(() => realtime.NotificationCreatedAsync(recipientId, notification, ct), recipientId);

    public Task RefreshUnreadCountAsync(Guid recipientId, CancellationToken ct) =>
        DeliverAsync(async () =>
        {
            var count = await repository.CountUnreadAsync(recipientId, ct);
            await realtime.UnreadCountChangedAsync(recipientId, new(count), ct);
        }, recipientId);

    private async Task DeliverAsync(Func<Task> send, Guid recipientId)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Real-time notification update failed for recipient {RecipientId}", recipientId);
        }
    }
}
