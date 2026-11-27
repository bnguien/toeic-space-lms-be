using ToeicSpace.Classroom.Application.DTOs.Student;

namespace ToeicSpace.Classroom.Application.Interfaces.Services;

public interface IStudentWorkspaceService
{
    Task<StudentPortalOverviewDto> GetOverviewAsync(Guid studentId, int daysAhead, CancellationToken ct);
    Task<ClassroomWorkspaceDto> GetWorkspaceAsync(Guid studentId, Guid classId, CancellationToken ct);
}
