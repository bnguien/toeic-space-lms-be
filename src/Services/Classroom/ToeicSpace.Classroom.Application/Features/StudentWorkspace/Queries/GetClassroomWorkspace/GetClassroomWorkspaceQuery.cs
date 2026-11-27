using MediatR;
using ToeicSpace.Classroom.Application.DTOs.Student;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetClassroomWorkspace;

public sealed record GetClassroomWorkspaceQuery(Guid StudentId, Guid ClassId) : IRequest<ClassroomWorkspaceDto>;
