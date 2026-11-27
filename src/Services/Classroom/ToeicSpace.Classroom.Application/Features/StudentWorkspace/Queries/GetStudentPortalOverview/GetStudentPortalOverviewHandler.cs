using MediatR;
using ToeicSpace.Classroom.Application.DTOs.Student;
using ToeicSpace.Classroom.Application.Interfaces.Services;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetStudentPortalOverview;

public sealed class GetStudentPortalOverviewHandler(IStudentWorkspaceService workspaceService)
    : IRequestHandler<GetStudentPortalOverviewQuery, StudentPortalOverviewDto>
{
    public Task<StudentPortalOverviewDto> Handle(GetStudentPortalOverviewQuery request, CancellationToken ct) =>
        workspaceService.GetOverviewAsync(request.StudentId, request.DaysAhead, ct);
}
