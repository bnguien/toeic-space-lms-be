namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAsRead;

public sealed record MarkAsReadCommand(Guid RecipientId, Guid Id) : IRequest<bool>;
