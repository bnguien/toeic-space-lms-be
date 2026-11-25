using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using ToeicSpace.BuildingBlocks.Security.Jwt;

namespace ToeicSpace.Notification.API.Security;

public sealed class NotificationIdentity : IUserIdProvider
{
    public const string Policy = "NotificationUser";

    public static Guid? UserId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(AccessTokenDefaults.SubjectClaim)?.Value, out var id) &&
            id != Guid.Empty ? id : null;

    public string? GetUserId(HubConnectionContext connection) => UserId(connection.User)?.ToString("D");
}
