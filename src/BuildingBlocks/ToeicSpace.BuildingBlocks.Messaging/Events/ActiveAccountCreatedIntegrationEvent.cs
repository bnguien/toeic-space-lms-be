namespace ToeicSpace.BuildingBlocks.Messaging.Events;

/// <summary>An Identity account was created in an active state.</summary>
public sealed record ActiveAccountCreatedIntegrationEvent(Guid UserId, DateTime OccurredOn) : IIntegrationEvent
{
    public Guid Id => UserId;
}
