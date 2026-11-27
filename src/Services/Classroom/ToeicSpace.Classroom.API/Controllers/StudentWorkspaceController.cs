using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToeicSpace.BuildingBlocks.Security.Jwt;
using ToeicSpace.Classroom.Application.DTOs.Student;
using ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetClassroomWorkspace;
using ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetStudentPortalOverview;
using ToeicSpace.Classroom.Domain.Exceptions;

namespace ToeicSpace.Classroom.API.Controllers;

[ApiController]
[Authorize(Roles = "User")]
public sealed class StudentWorkspaceController(ISender sender) : ControllerBase
{
    private Guid StudentId => Guid.TryParse(User.FindFirst(AccessTokenDefaults.SubjectClaim)?.Value, out var id)
        ? id : throw new ClassRuleException("invalid_subject", "Invalid identity.", 401);

    [HttpGet("api/v1/students/me/workspace")]
    public Task<StudentPortalOverviewDto> GetOverview(
        [FromQuery] int daysAhead = 14,
        CancellationToken ct = default) =>
        sender.Send(new GetStudentPortalOverviewQuery(StudentId, daysAhead), ct);

    [HttpGet("api/v1/classes/me/enrolled/{classId:guid}/workspace")]
    public Task<ClassroomWorkspaceDto> GetWorkspace(
        Guid classId,
        CancellationToken ct = default) =>
        sender.Send(new GetClassroomWorkspaceQuery(StudentId, classId), ct);
}
