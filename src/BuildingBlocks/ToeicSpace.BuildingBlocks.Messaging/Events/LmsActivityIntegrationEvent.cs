using System.Text.Json.Serialization;

namespace ToeicSpace.BuildingBlocks.Messaging.Events;

[JsonConverter(typeof(JsonStringEnumConverter<LmsActivity>))]
public enum LmsActivity
{
    StudentEnrolled,
    StudentWithdrawn,
    TeacherAssigned,
    TeacherUnassigned,
    ClassTeacherChanged,
    ClassStarted,
    ClassCompleted,
    ScheduleCreated,
    SessionAdded,
    SessionUpdated,
    SessionCancelled,
    MeetChanged,
    AttendanceAbsent,
    AttendanceAdjusted,
    ClassUpdated,
    StudentsJoined,
    AccountActivated,
    AccountSuspended,
    AccountUnlocked,
    PasswordChanged,
    RoleChanged,
    TuitionCreated,
    TuitionPaymentRecorded,
    TuitionWaived,
    ReconciliationRequired,
    GrammarCommentReplied,
}

// Recipient IDs are Identity subjects. Detail contains only user-visible context.
public sealed record LmsActivityIntegrationEvent(LmsActivity Activity, Guid[] RecipientIds,
    Guid? RelatedEntityId, string Detail) : IIntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}

public sealed record PaymentReconciliationRequested(Guid Id, DateTime OccurredOn, Guid TransactionId)
    : IIntegrationEvent;
