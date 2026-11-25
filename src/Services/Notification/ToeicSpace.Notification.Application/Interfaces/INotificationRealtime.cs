namespace ToeicSpace.Notification.Application.Interfaces;

public interface INotificationRealtime
{
    Task NotificationCreatedAsync(Guid recipientId, NotificationDto notification, CancellationToken ct);
    Task UnreadCountChangedAsync(Guid recipientId, UnreadCountDto unreadCount, CancellationToken ct);
}
