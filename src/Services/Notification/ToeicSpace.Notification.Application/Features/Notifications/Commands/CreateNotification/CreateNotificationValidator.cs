namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

public sealed class CreateNotificationValidator : AbstractValidator<CreateNotificationCommand>
{
    public CreateNotificationValidator()
    {
        RuleFor(x => x.RecipientIds).NotEmpty();
        RuleForEach(x => x.RecipientIds).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.Type).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Source).NotEmpty().MaximumLength(80);
        RuleFor(x => x.SourceEventId).NotEmpty();
        RuleFor(x => x.NavigationPath).MaximumLength(512)
            .Must(path => path is null || (path.StartsWith('/') && !path.StartsWith("//")
                && !path.Contains('\\') && !path.Any(char.IsControl)))
            .WithMessage("Notification navigation must be a local path.");
    }
}
