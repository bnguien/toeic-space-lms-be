namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotifications;

public sealed class GetNotificationsHandler(INotificationRepository repository)
    : IRequestHandler<GetNotificationsQuery, PaginatedResult<NotificationDto>>
{
    public Task<PaginatedResult<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken cancellationToken)
        => repository.ListAsync(request.RecipientId, request.IsRead, request.Type, request.PageIndex,
            request.PageSize, cancellationToken);
}
