namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

public sealed record CreateNotificationCommand(IReadOnlyCollection<Guid> RecipientIds,
    string Title, string Body, string Type, string Source, Guid SourceEventId, Guid? RelatedEntityId,
    string? NavigationPath) : IRequest;
