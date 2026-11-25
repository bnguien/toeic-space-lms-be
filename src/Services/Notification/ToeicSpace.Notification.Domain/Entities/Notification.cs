namespace ToeicSpace.Notification.Domain.Entities;

public sealed class Notification
{
    private Notification() { }

    public Notification(Guid recipientId, string title, string body, string type, string source,
        Guid sourceEventId, Guid? relatedEntityId, string? navigationPath, DateTime createdAt)
    {
        Id = Guid.NewGuid();
        RecipientId = recipientId;
        Title = title;
        Body = body;
        Type = type;
        Source = source;
        SourceEventId = sourceEventId;
        RelatedEntityId = relatedEntityId;
        NavigationPath = navigationPath;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid RecipientId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string Type { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public Guid SourceEventId { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? NavigationPath { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }
}
