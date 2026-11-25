namespace ToeicSpace.Notification.Application.Interfaces;

public interface INotificationRepository
{
    /// <summary>Returns false if this source event already produced this type for this recipient.</summary>
    Task<bool> TryInsertEventNotificationAsync(NotificationEntity notification, CancellationToken ct);
    Task<PaginatedResult<NotificationDto>> ListAsync(Guid recipientId, bool? isRead, string? type,
        int pageIndex, int pageSize, CancellationToken ct);
    Task<NotificationEntity?> FindAsync(Guid recipientId, Guid id, CancellationToken ct);
    Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct);
    Task<int> MarkReadAsync(Guid recipientId, Guid? id, DateTime readAt, CancellationToken ct);
}
