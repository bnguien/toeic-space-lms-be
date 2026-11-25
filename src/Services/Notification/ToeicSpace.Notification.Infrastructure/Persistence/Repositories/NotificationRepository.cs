using Microsoft.EntityFrameworkCore;
using ToeicSpace.BuildingBlocks.Pagination;
using ToeicSpace.Notification.Application.DTOs;
using ToeicSpace.Notification.Application.Interfaces;
using NotificationEntity = ToeicSpace.Notification.Domain.Entities.Notification;

namespace ToeicSpace.Notification.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository(NotificationDbContext db) : INotificationRepository
{
    public async Task<bool> TryInsertEventNotificationAsync(NotificationEntity notification, CancellationToken ct)
    {
        var entry = db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (PersistenceExceptionTranslator.IsDuplicateKey(exception))
        {
            // Confirm that the collision is the event key before treating the delivery as a retry.
            var alreadyProcessed = await db.Notifications.AnyAsync(x =>
                x.Source == notification.Source && x.SourceEventId == notification.SourceEventId &&
                x.RecipientId == notification.RecipientId && x.Type == notification.Type, ct);
            if (!alreadyProcessed)
                throw;

            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<PaginatedResult<NotificationDto>> ListAsync(Guid recipientId, bool? isRead, string? type,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = ForRecipient(recipientId);
        if (isRead is { } read)
            query = query.Where(x => x.ReadAt.HasValue == read);
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(x => x.Type == type);

        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(NotificationDto.From).ToArray(), count, pageIndex, pageSize);
    }

    public Task<NotificationEntity?> FindAsync(Guid recipientId, Guid id, CancellationToken ct) =>
        ForRecipient(recipientId).SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct) =>
        ForRecipient(recipientId).CountAsync(x => x.ReadAt == null, ct);

    public Task<int> MarkReadAsync(Guid recipientId, Guid? id, DateTime readAt, CancellationToken ct)
    {
        var unread = ForRecipient(recipientId).Where(x => x.ReadAt == null);
        if (id is { } notificationId)
            unread = unread.Where(x => x.Id == notificationId);

        return unread.ExecuteUpdateAsync(updates => updates.SetProperty(x => x.ReadAt, (DateTime?)readAt), ct);
    }

    private IQueryable<NotificationEntity> ForRecipient(Guid recipientId) =>
        db.Notifications.AsNoTracking().Where(x => x.RecipientId == recipientId);
}
