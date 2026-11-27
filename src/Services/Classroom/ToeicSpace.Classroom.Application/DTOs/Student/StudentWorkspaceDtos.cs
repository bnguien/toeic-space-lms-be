using ToeicSpace.Classroom.Domain.Enums;

namespace ToeicSpace.Classroom.Application.DTOs.Student;

public sealed record StudentUpcomingSessionDto(
    Guid SessionId,
    Guid ClassId,
    string ClassCode,
    string ClassName,
    string Title,
    DateTime StartAt,
    DateTime EndAt,
    SessionMode Mode,
    string? RoomName,
    string? TeacherName,
    SessionStatus Status);

public sealed record StudentAttendanceSummaryDto(
    int TotalRecorded,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int ExcusedCount);

public sealed record StudentEnrolledClassSummaryDto(
    Guid ClassId,
    string Code,
    string Name,
    ClassStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? TeacherName,
    string? DefaultRoomName);

public sealed record StudentPortalOverviewDto(
    Guid StudentId,
    IReadOnlyList<StudentEnrolledClassSummaryDto> EnrolledClasses,
    IReadOnlyList<StudentUpcomingSessionDto> UpcomingSessions,
    StudentAttendanceSummaryDto AttendanceSummary);

public sealed record StudentSessionWorkspaceDto(
    Guid SessionId,
    string Title,
    DateTime StartAt,
    DateTime EndAt,
    SessionMode Mode,
    string? RoomName,
    string? TeacherName,
    SessionStatus Status,
    AttendanceStatus? MyAttendanceStatus,
    string? MyAttendanceNote);

public sealed record StudentClassScheduleDto(
    Guid ScheduleId,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    SessionMode Mode,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo);

public sealed record ClassroomWorkspaceDto(
    Guid ClassId,
    string Code,
    string Name,
    string? Description,
    ClassStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? TeacherName,
    string? DefaultRoomName,
    IReadOnlyList<StudentClassScheduleDto> Schedules,
    IReadOnlyList<StudentSessionWorkspaceDto> Sessions);
