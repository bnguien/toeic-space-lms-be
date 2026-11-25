using MassTransit;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Identity.Domain.Enums;
using ToeicSpace.Identity.Infrastructure.Persistence;

namespace ToeicSpace.Identity.Infrastructure.Consumers;

public sealed class PaymentReconciliationConsumer(IdentityDbContext db) : IConsumer<PaymentReconciliationRequested>
{
    public async Task Consume(ConsumeContext<PaymentReconciliationRequested> context)
    {
        var message = context.Message;
        var admins = await db.Users.Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active &&
            u.DeletedAt == null).Select(u => u.Id).ToArrayAsync(context.CancellationToken);
        if (admins.Length > 0)
            await context.Publish(new LmsActivityIntegrationEvent(LmsActivity.ReconciliationRequired, admins,
                message.TransactionId, "") { Id = message.Id, OccurredOn = message.OccurredOn }, context.CancellationToken);
    }
}
