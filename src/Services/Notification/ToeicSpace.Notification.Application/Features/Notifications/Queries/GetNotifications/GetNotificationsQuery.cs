namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotifications;

public sealed record GetNotificationsQuery(Guid RecipientId, int PageIndex = 1, int PageSize = 20,
    bool? IsRead = null, string? Type = null) : IRequest<PaginatedResult<NotificationDto>>;
