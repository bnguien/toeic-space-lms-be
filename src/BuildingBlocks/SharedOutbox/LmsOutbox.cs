using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToeicSpace.BuildingBlocks.Messaging;
using ToeicSpace.BuildingBlocks.Messaging.Events;

namespace ToeicSpace.BuildingBlocks.Persistence;

// Compiled into each producer infrastructure assembly, using its own DbContext and transaction.
internal sealed class LmsOutboxMessage
{
    public Guid Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public string Contract { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTime? ProcessedOn { get; set; }
    public int Attempts { get; set; }

    public static void Configure(ModelBuilder model)
    {
        var entity = model.Entity<LmsOutboxMessage>();
        entity.ToTable("LmsOutboxMessages");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Contract).HasMaxLength(100);
        entity.HasIndex(x => new { x.ProcessedOn, x.OccurredOn });
    }

    public static void Add<T>(DbContext db, T message) where T : IIntegrationEvent =>
        db.Set<LmsOutboxMessage>().Add(new()
        {
            Id = message.Id, OccurredOn = message.OccurredOn, Contract = typeof(T).Name,
            Payload = JsonSerializer.Serialize(message)
        });
}

internal sealed class LmsOutboxDispatcher<TContext>(IServiceScopeFactory scopes, IBus bus,
    ILogger<LmsOutboxDispatcher<TContext>> logger) : BackgroundService where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                var rows = await db.Set<LmsOutboxMessage>()
                    .Where(x => x.ProcessedOn == null && x.Attempts < 50)
                    .OrderBy(x => x.OccurredOn).Take(50).ToListAsync(ct);
                foreach (var row in rows)
                {
                    try
                    {
                        var type = row.Contract switch
                        {
                            nameof(LmsActivityIntegrationEvent) => typeof(LmsActivityIntegrationEvent),
                            nameof(PaymentReconciliationRequested) => typeof(PaymentReconciliationRequested),
                            _ => throw new InvalidOperationException("Unknown outbox contract.")
                        };
                        var message = JsonSerializer.Deserialize(row.Payload, type)!;
                        await bus.Publish(message, type, ct);
                        row.ProcessedOn = DateTime.UtcNow;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        row.Attempts++;
                        logger.LogWarning(ex, "Failed to dispatch outbox event {EventId}, attempt {Attempt}", row.Id, row.Attempts);
                    }
                }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed outbox dispatch cycle");
            }
        }
    }
}
