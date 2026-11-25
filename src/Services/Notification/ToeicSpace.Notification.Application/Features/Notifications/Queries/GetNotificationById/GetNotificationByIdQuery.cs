namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotificationById;

public sealed record GetNotificationByIdQuery(Guid RecipientId, Guid Id) : IRequest<NotificationDto?>;
