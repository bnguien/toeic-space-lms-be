using ToeicSpace.BuildingBlocks.Messaging;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Identity.Application.Interfaces.Persistence;
using ToeicSpace.Identity.Domain.Entities;
using ToeicSpace.Identity.Domain.Enums;
using ToeicSpace.Identity.Infrastructure.Messaging.Outbox;

namespace ToeicSpace.Identity.Infrastructure.Persistence;

public class IdentityDbContext : DbContext, IIdentityDbContext
{
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserToken> UserTokens => Set<UserToken>();

    public DbSet<UserOAuth> UserOAuthAccounts => Set<UserOAuth>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public void EnqueueIntegrationEvent(IIntegrationEvent message) => OutboxMessages.Add(IdentityOutbox.Create(message));

    public override int SaveChanges()
    {
        CaptureUserChanges();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        CaptureUserChanges();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    private void CaptureUserChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<User>().ToArray())
        {
            var relevantChange = entry.State is EntityState.Added or EntityState.Deleted
                || entry.State == EntityState.Modified && new[]
                {
                    nameof(User.FullName), nameof(User.Email), nameof(User.AvatarUrl),
                    nameof(User.Role), nameof(User.Status), nameof(User.DeletedAt), nameof(User.EmailVerifiedAt)
                }.Any(name => entry.Property(name).IsModified);
            if (!relevantChange) continue;

            var user = entry.Entity;
            user.Version = entry.State == EntityState.Added
                ? Math.Max(1, user.Version)
                : entry.Property(x => x.Version).OriginalValue + 1;

            // Active accounts can sign in and view their notification. Synchronization snapshots
            // do not emit this event. Keep the event in the same transaction as the new account.
            if (entry.State == EntityState.Added && user.Status == UserStatus.Active &&
                user.DeletedAt is null && OutboxMessages.Local.All(message => message.Id != user.Id))
            {
                EnqueueIntegrationEvent(new ActiveAccountCreatedIntegrationEvent(user.Id, user.CreatedAt));
            }

            if (entry.State == EntityState.Modified && user.DeletedAt is null)
            {
                var previous = entry.Property(x => x.Status).OriginalValue;
                var activity = (previous, user.Status) switch
                {
                    (UserStatus.Suspended, UserStatus.Active) => LmsActivity.AccountUnlocked,
                    (UserStatus.Inactive, UserStatus.Active) => LmsActivity.AccountActivated,
                    (_, UserStatus.Suspended) when previous != user.Status => LmsActivity.AccountSuspended,
                    _ => (LmsActivity?)null
                };
                if (activity.HasValue)
                    EnqueueIntegrationEvent(new LmsActivityIntegrationEvent(activity.Value, [user.Id], user.Id, ""));
                if (entry.Property(x => x.Role).OriginalValue != user.Role)
                    EnqueueIntegrationEvent(new LmsActivityIntegrationEvent(LmsActivity.RoleChanged, [user.Id], user.Id, ""));
            }

            var deleted = entry.State == EntityState.Deleted || user.DeletedAt is not null;
            EnqueueIntegrationEvent(new TeacherChangedIntegrationEvent(
                user.Id, user.FullName, user.Email, user.AvatarUrl,
                user.Role.ToString(), deleted ? "Deleted" : user.Status.ToString(),
                !deleted && user.Role == UserRole.Teacher && user.Status == UserStatus.Active,
                user.Version));
            EnqueueIntegrationEvent(new StudentChangedIntegrationEvent(
                user.Id, user.FullName, user.Email, user.Role.ToString(),
                deleted ? "Deleted" : user.Status.ToString(),
                !deleted && user.EmailVerifiedAt is not null,
                !deleted && user.Role == UserRole.User && user.Status == UserStatus.Active && user.EmailVerifiedAt is not null,
                user.Version));
        }
    }
}
