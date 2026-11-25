using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Notification.Infrastructure.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using ToeicSpace.BuildingBlocks.Pagination;
using ToeicSpace.Notification.Application.DTOs;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;
using ToeicSpace.Notification.Application.Interfaces;
using ToeicSpace.Notification.Application.Services;
using Xunit;
using NotificationEntity = ToeicSpace.Notification.Domain.Entities.Notification;

namespace ToeicSpace.Notification.Tests;

public sealed class CreateNotificationTests
{
    [Fact]
    public void Every_activity_has_a_stable_type_and_preserves_recipients_and_event_identity()
    {
        foreach (var activity in Enum.GetValues<LmsActivity>())
        {
            var message = new LmsActivityIntegrationEvent(activity, [Guid.NewGuid()], Guid.NewGuid(), "Lớp TOEIC 650");
            var command = LmsActivityConsumer.Map(message);
            Assert.Equal(message.Id, command.SourceEventId);
            Assert.Equal(message.RecipientIds, command.RecipientIds);
            Assert.Equal(activity.ToString(), command.Type);
            Assert.Equal(message.RelatedEntityId, command.RelatedEntityId);
            Assert.Contains(message.Detail, command.Body);
            Assert.True(new CreateNotificationValidator().Validate(command).IsValid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistsEachRecipientBeforeBestEffortDelivery(bool deliveryFails)
    {
        var repository = new RecordingRepository();
        var realtime = new RecordingRealtime(repository, deliveryFails);
        var delivery = new NotificationDelivery(realtime, repository, NullLogger<NotificationDelivery>.Instance);
        var handler = new CreateNotificationHandler(repository, delivery, TimeProvider.System);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var command = new CreateNotificationCommand([first, second, first], "Tiêu đề", "Nội dung",
            "Created", "Tests", Guid.NewGuid(), null, "/results");

        await handler.Handle(command with { RecipientIds = [first] }, default);
        await handler.Handle(command, default);

        await handler.Handle(command with { Type = "Reminder", RecipientIds = [first] }, default);
        await handler.Handle(command with { SourceEventId = Guid.NewGuid(), RecipientIds = [first] }, default);

        Assert.Equal(4, repository.Rows.Count);
        Assert.Equal(new[] { first, second, first, first }, realtime.Recipients);
        Assert.All(repository.Rows, row => Assert.Null(row.ReadAt));
    }

    [Theory]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    [InlineData("https://example.com")]
    public void RejectsExternalNavigation(string path)
    {
        var command = new CreateNotificationCommand([Guid.NewGuid()], "Tiêu đề", "Nội dung",
            "Created", "Tests", Guid.NewGuid(), null, path);
        Assert.False(new CreateNotificationValidator().Validate(command).IsValid);
    }

    private sealed class RecordingRealtime(RecordingRepository repository, bool deliveryFails) : INotificationRealtime
    {
        public List<Guid> Recipients { get; } = [];
        public Task NotificationCreatedAsync(Guid recipientId, NotificationDto notification, CancellationToken ct)
        {
            Assert.Contains(repository.Rows, row => row.Id == notification.Id && row.RecipientId == recipientId);
            Recipients.Add(recipientId);
            if (deliveryFails)
                throw new InvalidOperationException("Transport unavailable.");
            return Task.CompletedTask;
        }
        public Task UnreadCountChangedAsync(Guid recipientId, UnreadCountDto count, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class RecordingRepository : INotificationRepository
    {
        public List<NotificationEntity> Rows { get; } = [];
        public Task<bool> TryInsertEventNotificationAsync(NotificationEntity notification, CancellationToken ct)
        {
            if (Rows.Any(x => x.Source == notification.Source && x.SourceEventId == notification.SourceEventId
                && x.RecipientId == notification.RecipientId && x.Type == notification.Type))
                return Task.FromResult(false);

            Rows.Add(notification);
            return Task.FromResult(true);
        }

        public Task<int> CountUnreadAsync(Guid recipientId, CancellationToken ct) =>
            Task.FromResult(Rows.Count(x => x.RecipientId == recipientId && !x.ReadAt.HasValue));
        public Task<PaginatedResult<NotificationDto>> ListAsync(Guid recipientId, bool? isRead,
            string? type, int pageIndex, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<NotificationEntity?> FindAsync(Guid recipientId, Guid id, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> MarkReadAsync(Guid recipientId, Guid? id, DateTime readAt, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
