using MediatR;
using ToeicSpace.Classroom.Application.DTOs.Student;
using ToeicSpace.Classroom.Application.Interfaces.Services;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetClassroomWorkspace;

public sealed class GetClassroomWorkspaceHandler(IStudentWorkspaceService workspaceService)
    : IRequestHandler<GetClassroomWorkspaceQuery, ClassroomWorkspaceDto>
{
    public Task<ClassroomWorkspaceDto> Handle(GetClassroomWorkspaceQuery request, CancellationToken ct) =>
        workspaceService.GetWorkspaceAsync(request.StudentId, request.ClassId, ct);
}
