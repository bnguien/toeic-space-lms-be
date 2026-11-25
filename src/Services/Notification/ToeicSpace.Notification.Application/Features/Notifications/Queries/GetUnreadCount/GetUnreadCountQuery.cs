namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetUnreadCount;

public sealed record GetUnreadCountQuery(Guid RecipientId) : IRequest<UnreadCountDto>;
