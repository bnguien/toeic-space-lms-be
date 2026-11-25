namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotificationById;

public sealed class GetNotificationByIdHandler(INotificationRepository repository)
    : IRequestHandler<GetNotificationByIdQuery, NotificationDto?>
{
    public async Task<NotificationDto?> Handle(GetNotificationByIdQuery request, CancellationToken cancellationToken)
    {
        var notification = await repository.FindAsync(request.RecipientId, request.Id, cancellationToken);
        return notification is null ? null : NotificationDto.From(notification);
    }
}
