using Microsoft.EntityFrameworkCore;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.BuildingBlocks.Persistence;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Domain.Enums;

namespace ToeicSpace.Classroom.Infrastructure.Persistence;

public sealed partial class ClassroomDbContext
{
    public bool SuppressActivityEvents { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (SuppressActivityEvents)
            return await base.SaveChangesAsync(ct);

        var events = await CaptureActivitiesAsync(ct);
        foreach (var message in events)
            LmsOutboxMessage.Add(this, message);
        try
        {
            return await base.SaveChangesAsync(ct);
        }
        catch
        {
            var ids = events.Select(x => x.Id).ToHashSet();
            foreach (var pending in ChangeTracker.Entries<LmsOutboxMessage>().Where(x => ids.Contains(x.Entity.Id)).ToArray())
                pending.State = EntityState.Detached;
            throw;
        }
    }

    private async Task<List<LmsActivityIntegrationEvent>> CaptureActivitiesAsync(CancellationToken ct)
    {
        var events = new List<LmsActivityIntegrationEvent>();
        var audience = new Dictionary<Guid, Guid[]>();
        var names = new Dictionary<Guid, string>();
        async Task Add(LmsActivity type, Guid classId, Guid relatedId, Guid[]? recipients = null,
            Guid? sessionTeacher = null, bool includeClassTeacher = true)
        {
            if (recipients == null)
            {
                if (!audience.TryGetValue(classId, out var students))
                {
                    students = await ClassEnrollments.Where(x => x.ClassId == classId &&
                        x.Status == EnrollmentStatus.Active && x.WithdrawnAt == null).Select(x => x.StudentId).ToArrayAsync(ct);
                    audience.Add(classId, students);
                }
                var teacher = sessionTeacher;
                if (!teacher.HasValue && includeClassTeacher)
                    teacher = await ClassInstructors.Where(x => x.ClassId == classId && x.UnassignedAt == null)
                        .Select(x => (Guid?)x.TeacherId).FirstOrDefaultAsync(ct);
                recipients = teacher is { } id ? students.Append(id).Distinct().ToArray() : students;
            }
            if (recipients.Length == 0) return;
            if (!names.TryGetValue(classId, out var name))
            {
                name = Classes.Local.FirstOrDefault(x => x.Id == classId)?.Name ??
                    await Classes.Where(x => x.Id == classId).Select(x => x.Name).FirstAsync(ct);
                names.Add(classId, name);
            }
            events.Add(new(type, recipients, relatedId, $"Lớp: {name}."));
        }

        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var added = entry.State == EntityState.Added;
            bool Changed(string field) => !added && entry.Property(field).IsModified &&
                !Equals(entry.Property(field).OriginalValue, entry.Property(field).CurrentValue);
            switch (entry.Entity)
            {
                case ClassEnrollment e when added && e.Status == EnrollmentStatus.Active && e.WithdrawnAt == null:
                    await Add(LmsActivity.StudentEnrolled, e.ClassId, e.ClassId, [e.StudentId]);
                    break;
                case ClassEnrollment e when Changed(nameof(e.Status)) && e.Status == EnrollmentStatus.Withdrawn:
                    await Add(LmsActivity.StudentWithdrawn, e.ClassId, e.ClassId, [e.StudentId]);
                    break;
                case ClassInstructor i when added && i.UnassignedAt == null:
                    await Add(LmsActivity.TeacherAssigned, i.ClassId, i.ClassId, [i.TeacherId]);
                    await Add(LmsActivity.ClassTeacherChanged, i.ClassId, i.ClassId, includeClassTeacher: false);
                    break;
                case ClassInstructor i when Changed(nameof(i.UnassignedAt)) && i.UnassignedAt.HasValue:
                    await Add(LmsActivity.TeacherUnassigned, i.ClassId, i.ClassId, [i.TeacherId]);
                    break;
                case Class c when Changed(nameof(c.Status)):
                    if (c.Status == ClassStatus.Active && Equals(entry.Property(nameof(c.Status)).OriginalValue, ClassStatus.Draft))
                        await Add(LmsActivity.ClassStarted, c.Id, c.Id);
                    if (c.Status == ClassStatus.Completed && Equals(entry.Property(nameof(c.Status)).OriginalValue, ClassStatus.Active))
                        await Add(LmsActivity.ClassCompleted, c.Id, c.Id);
                    break;
                case Class c when Changed(nameof(c.Name)) || Changed(nameof(c.StartDate)) ||
                    Changed(nameof(c.EndDate)) || Changed(nameof(c.DefaultRoomId)):
                    await Add(LmsActivity.ClassUpdated, c.Id, c.Id);
                    break;
                case ClassSchedule schedule when added:
                    await Add(LmsActivity.ScheduleCreated, schedule.ClassId, schedule.Id, sessionTeacher: schedule.TeacherId);
                    break;
                case ClassSession session:
                    LmsActivity? activity = null;
                    if (added && session.ClassScheduleId == null) activity = LmsActivity.SessionAdded;
                    else if (Changed(nameof(session.Status)) && session.Status == SessionStatus.Cancelled) activity = LmsActivity.SessionCancelled;
                    else if (Changed(nameof(session.StartAt)) || Changed(nameof(session.EndAt)) || Changed(nameof(session.RoomId)))
                        activity = LmsActivity.SessionUpdated;
                    if (activity.HasValue)
                        await Add(activity.Value, session.ClassId, session.Id, sessionTeacher: session.TeacherId);
                    break;
                case AttendanceAudit audit when audit.PreviousStatus != audit.NewStatus:
                    if (audit.PreviousStatus != null || audit.NewStatus == AttendanceStatus.Absent)
                        events.Add(new(audit.PreviousStatus == null ? LmsActivity.AttendanceAbsent : LmsActivity.AttendanceAdjusted,
                            [audit.StudentId], audit.ClassSessionId, ""));
                    break;
                case SessionMeetProvisioning meet when Changed(nameof(meet.MeetingUrl)) || (added && meet.MeetingUrl != null):
                    var lesson = await ClassSessions.AsNoTracking().SingleAsync(x => x.Id == meet.ClassSessionId, ct);
                    await Add(LmsActivity.MeetChanged, lesson.ClassId, lesson.Id, sessionTeacher: lesson.TeacherId);
                    break;
            }
        }
        return events;
    }
}
