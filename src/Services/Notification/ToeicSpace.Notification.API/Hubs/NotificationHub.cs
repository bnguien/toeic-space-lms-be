using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ToeicSpace.Notification.API.Security;
using ToeicSpace.Notification.Application.DTOs;
using ToeicSpace.Notification.Application.Interfaces;

namespace ToeicSpace.Notification.API.Hubs;

public interface INotificationClient
{
    Task NotificationCreated(NotificationDto notification);
    Task UnreadCountChanged(UnreadCountDto unreadCount);
}

[Authorize(Policy = NotificationIdentity.Policy)]
public sealed class NotificationHub : Hub<INotificationClient>
{
    public const string Path = "/hubs/notifications";
}

public sealed class SignalRNotificationRealtime(IHubContext<NotificationHub, INotificationClient> hub)
    : INotificationRealtime
{
    public Task NotificationCreatedAsync(Guid recipientId, NotificationDto notification, CancellationToken ct) =>
        hub.Clients.User(recipientId.ToString("D")).NotificationCreated(notification).WaitAsync(ct);

    public Task UnreadCountChangedAsync(Guid recipientId, UnreadCountDto unreadCount, CancellationToken ct) =>
        hub.Clients.User(recipientId.ToString("D")).UnreadCountChanged(unreadCount).WaitAsync(ct);
}
