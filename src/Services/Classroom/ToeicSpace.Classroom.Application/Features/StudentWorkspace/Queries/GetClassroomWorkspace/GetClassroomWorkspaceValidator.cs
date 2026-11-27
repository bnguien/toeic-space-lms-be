using FluentValidation;

namespace ToeicSpace.Classroom.Application.Features.StudentWorkspace.Queries.GetClassroomWorkspace;

public sealed class GetClassroomWorkspaceValidator : AbstractValidator<GetClassroomWorkspaceQuery>
{
    public GetClassroomWorkspaceValidator()
    {
        RuleFor(x => x.StudentId)
            .NotEmpty().WithMessage("Student ID is required.");

        RuleFor(x => x.ClassId)
            .NotEmpty().WithMessage("Class ID is required.");
    }
}
