using MediatR;
using ToeicSpace.Classroom.Application.DTOs.Student;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetStudentPortalOverview;

public sealed record GetStudentPortalOverviewQuery(Guid StudentId, int DaysAhead = 14) : IRequest<StudentPortalOverviewDto>;
