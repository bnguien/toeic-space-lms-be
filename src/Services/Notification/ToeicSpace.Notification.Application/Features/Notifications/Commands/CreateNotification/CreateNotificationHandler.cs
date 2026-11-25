namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

public sealed class CreateNotificationHandler(INotificationRepository repository,
    Services.NotificationDelivery delivery, TimeProvider clock) : IRequestHandler<CreateNotificationCommand>
{
    public async Task Handle(CreateNotificationCommand request, CancellationToken cancellationToken)
    {
        var createdAt = clock.GetUtcNow().UtcDateTime;
        foreach (var recipient in request.RecipientIds.Distinct())
        {
            var notification = new NotificationEntity(recipient, request.Title, request.Body, request.Type,
                request.Source, request.SourceEventId, request.RelatedEntityId, request.NavigationPath, createdAt);
            if (!await repository.TryInsertEventNotificationAsync(notification, cancellationToken))
                continue;

            await delivery.CreatedAsync(recipient, NotificationDto.From(notification), cancellationToken);
            await delivery.RefreshUnreadCountAsync(recipient, cancellationToken);
        }
    }
}
