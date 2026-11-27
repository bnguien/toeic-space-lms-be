using FluentValidation;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetStudentPortalOverview;

public sealed class GetStudentPortalOverviewValidator : AbstractValidator<GetStudentPortalOverviewQuery>
{
    public GetStudentPortalOverviewValidator()
    {
        RuleFor(x => x.StudentId)
            .NotEmpty().WithMessage("Student ID is required.");

        RuleFor(x => x.DaysAhead)
            .InclusiveBetween(1, 60).WithMessage("Days ahead must be between 1 and 60.");
    }
}
