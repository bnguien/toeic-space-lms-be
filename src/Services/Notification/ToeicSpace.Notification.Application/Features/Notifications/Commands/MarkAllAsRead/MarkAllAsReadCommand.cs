namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAllAsRead;

public sealed record MarkAllAsReadCommand(Guid RecipientId) : IRequest;
