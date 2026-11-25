namespace ToeicSpace.Notification.Application.Features.Notifications.Queries.GetUnreadCount;

public sealed class GetUnreadCountHandler(INotificationRepository repository)
    : IRequestHandler<GetUnreadCountQuery, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(GetUnreadCountQuery request, CancellationToken cancellationToken)
        => new(await repository.CountUnreadAsync(request.RecipientId, cancellationToken));
}
