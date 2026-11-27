using Microsoft.EntityFrameworkCore;
using ToeicSpace.Classroom.Application.DTOs.Student;
using ToeicSpace.Classroom.Application.Interfaces.Services;
using ToeicSpace.Classroom.Domain.Entities;
using ToeicSpace.Classroom.Domain.Enums;
using ToeicSpace.Classroom.Domain.Exceptions;
using ToeicSpace.Classroom.Infrastructure.Persistence;

namespace ToeicSpace.Classroom.Infrastructure.Services.StudentWorkspace;

public sealed class StudentWorkspaceService(
    ClassroomDbContext db,
    TimeProvider clock) : IStudentWorkspaceService
{
    public async Task<StudentPortalOverviewDto> GetOverviewAsync(Guid studentId, int daysAhead, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var maxDate = now.AddDays(daysAhead);

        var enrolledClassIds = await db.ClassEnrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active && e.WithdrawnAt == null)
            .Select(e => e.ClassId)
            .ToListAsync(ct);

        var classes = await db.Classes.AsNoTracking()
            .Where(c => enrolledClassIds.Contains(c.Id) && c.Status != ClassStatus.Deleted)
            .ToListAsync(ct);

        var classIds = classes.Select(c => c.Id).ToList();

        var instructors = await db.ClassInstructors.AsNoTracking()
            .Where(i => classIds.Contains(i.ClassId) && i.UnassignedAt == null)
            .Join(db.Teachers.AsNoTracking(), i => i.TeacherId, t => t.TeacherId, (i, t) => new { i.ClassId, t.Name })
            .ToDictionaryAsync(x => x.ClassId, x => x.Name, ct);

        var roomIds = classes.Where(c => c.DefaultRoomId.HasValue).Select(c => c.DefaultRoomId!.Value).Distinct().ToList();
        var rooms = await db.Rooms.AsNoTracking()
            .Where(r => roomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var classSummaries = classes.Select(c => new StudentEnrolledClassSummaryDto(
            c.Id,
            c.Code,
            c.Name,
            c.Status,
            c.StartDate,
            c.EndDate,
            instructors.GetValueOrDefault(c.Id),
            c.DefaultRoomId.HasValue ? rooms.GetValueOrDefault(c.DefaultRoomId.Value) : null
        )).ToList();

        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => classIds.Contains(s.ClassId) && s.StartAt >= now && s.StartAt <= maxDate && s.Status != SessionStatus.Cancelled)
            .OrderBy(s => s.StartAt)
            .Take(20)
            .ToListAsync(ct);

        var sessionRoomIds = sessions.Where(s => s.RoomId.HasValue).Select(s => s.RoomId!.Value).Distinct().ToList();
        var sessionRooms = await db.Rooms.AsNoTracking()
            .Where(r => sessionRoomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var teacherIds = sessions.Select(s => s.TeacherId).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.TeacherId))
            .ToDictionaryAsync(t => t.TeacherId, t => t.Name, ct);

        var classDict = classes.ToDictionary(c => c.Id);

        var upcomingDtos = sessions.Select(s => new StudentUpcomingSessionDto(
            s.Id,
            s.ClassId,
            classDict.TryGetValue(s.ClassId, out var cls) ? cls.Code : "",
            cls?.Name ?? "",
            s.Title,
            s.StartAt,
            s.EndAt,
            s.Mode,
            s.RoomId.HasValue ? sessionRooms.GetValueOrDefault(s.RoomId.Value) : null,
            teachers.GetValueOrDefault(s.TeacherId),
            s.Status
        )).ToList();

        var records = await db.Set<AttendanceRecord>().AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .ToListAsync(ct);

        var summary = new StudentAttendanceSummaryDto(
            records.Count,
            records.Count(r => r.Status == AttendanceStatus.Present),
            records.Count(r => r.Status == AttendanceStatus.Late),
            records.Count(r => r.Status == AttendanceStatus.Absent),
            records.Count(r => r.Status == AttendanceStatus.Excused)
        );

        return new StudentPortalOverviewDto(studentId, classSummaries, upcomingDtos, summary);
    }

    public async Task<ClassroomWorkspaceDto> GetWorkspaceAsync(Guid studentId, Guid classId, CancellationToken ct)
    {
        var isEnrolled = await db.ClassEnrollments.AsNoTracking()
            .AnyAsync(e => e.ClassId == classId && e.StudentId == studentId && e.Status == EnrollmentStatus.Active && e.WithdrawnAt == null, ct);

        if (!isEnrolled)
        {
            throw new ClassRuleException("forbidden_class", "No access to this class.", 403);
        }

        var cls = await db.Classes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == classId && c.Status != ClassStatus.Deleted, ct)
            ?? throw ClassRuleException.NotFound();

        var teacherName = await db.ClassInstructors.AsNoTracking()
            .Where(i => i.ClassId == classId && i.UnassignedAt == null)
            .Join(db.Teachers.AsNoTracking(), i => i.TeacherId, t => t.TeacherId, (i, t) => t.Name)
            .FirstOrDefaultAsync(ct);

        string? roomName = null;
        if (cls.DefaultRoomId.HasValue)
        {
            roomName = await db.Rooms.AsNoTracking()
                .Where(r => r.Id == cls.DefaultRoomId.Value)
                .Select(r => r.Name)
                .FirstOrDefaultAsync(ct);
        }

        var schedules = await db.ClassSchedules.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .Select(s => new StudentClassScheduleDto(
                s.Id,
                s.DaysOfWeek,
                s.StartTime,
                s.EndTime,
                s.Mode,
                s.EffectiveFrom,
                s.EffectiveTo))
            .ToListAsync(ct);

        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.StartAt)
            .ToListAsync(ct);

        var sessionRoomIds = sessions.Where(s => s.RoomId.HasValue).Select(s => s.RoomId!.Value).Distinct().ToList();
        var sessionRooms = await db.Rooms.AsNoTracking()
            .Where(r => sessionRoomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var teacherIds = sessions.Select(s => s.TeacherId).Distinct().ToList();
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.TeacherId))
            .ToDictionaryAsync(t => t.TeacherId, t => t.Name, ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var attendanceRecords = await db.Set<AttendanceRecord>().AsNoTracking()
            .Where(a => a.StudentId == studentId && sessionIds.Contains(a.ClassSessionId))
            .ToDictionaryAsync(a => a.ClassSessionId, ct);

        var sessionDtos = sessions.Select(s =>
        {
            attendanceRecords.TryGetValue(s.Id, out var att);
            return new StudentSessionWorkspaceDto(
                s.Id,
                s.Title,
                s.StartAt,
                s.EndAt,
                s.Mode,
                s.RoomId.HasValue ? sessionRooms.GetValueOrDefault(s.RoomId.Value) : null,
                teachers.GetValueOrDefault(s.TeacherId) ?? teacherName,
                s.Status,
                att?.Status,
                att?.Note
            );
        }).ToList();

        return new ClassroomWorkspaceDto(
            cls.Id,
            cls.Code,
            cls.Name,
            cls.Description,
            cls.Status,
            cls.StartDate,
            cls.EndDate,
            teacherName,
            roomName,
            schedules,
            sessionDtos
        );
    }
}
