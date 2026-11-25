using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToeicSpace.BuildingBlocks.Pagination;
using ToeicSpace.Notification.API.Security;
using ToeicSpace.Notification.Application.DTOs;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAllAsRead;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAsRead;
using ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotificationById;
using ToeicSpace.Notification.Application.Features.Notifications.Queries.GetNotifications;
using ToeicSpace.Notification.Application.Features.Notifications.Queries.GetUnreadCount;

namespace ToeicSpace.Notification.API.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize(Policy = NotificationIdentity.Policy)]
public sealed class NotificationsController(ISender sender) : ControllerBase
{
    private Guid RecipientId => NotificationIdentity.UserId(User)!.Value;

    [HttpGet]
    public Task<PaginatedResult<NotificationDto>> List([FromQuery] bool? isRead, [FromQuery] string? type,
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        sender.Send(new GetNotificationsQuery(RecipientId, pageIndex, pageSize, isRead, type), ct);

    [HttpGet("unread-count")]
    public Task<UnreadCountDto> UnreadCount(CancellationToken ct) =>
        sender.Send(new GetUnreadCountQuery(RecipientId), ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> Get(Guid id, CancellationToken ct)
    {
        var notification = await sender.Send(new GetNotificationByIdQuery(RecipientId, id), ct);
        if (notification is null)
            return NotFound();

        return notification;
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken ct) =>
        await sender.Send(new MarkAsReadCommand(RecipientId, id), ct) ? NoContent() : NotFound();

    [HttpPatch("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        await sender.Send(new MarkAllAsReadCommand(RecipientId), ct);
        return NoContent();
    }
}
