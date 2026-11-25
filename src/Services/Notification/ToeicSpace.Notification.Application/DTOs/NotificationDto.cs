namespace ToeicSpace.Notification.Application.DTOs;

public sealed record NotificationDto(Guid Id, string Title, string Body, string Type, string Source,
    Guid? RelatedEntityId, string? NavigationPath, DateTime CreatedAt, DateTime? ReadAt)
{
    public bool IsRead => ReadAt.HasValue;

    public static NotificationDto From(NotificationEntity entity) => new(entity.Id, entity.Title,
        entity.Body, entity.Type, entity.Source, entity.RelatedEntityId, entity.NavigationPath,
        DateTime.SpecifyKind(entity.CreatedAt, DateTimeKind.Utc),
        entity.ReadAt is { } readAt ? DateTime.SpecifyKind(readAt, DateTimeKind.Utc) : null);
}

public sealed record UnreadCountDto(int Count);
