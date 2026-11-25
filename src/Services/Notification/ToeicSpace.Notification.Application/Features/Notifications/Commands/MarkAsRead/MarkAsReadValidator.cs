namespace ToeicSpace.Notification.Application.Features.Notifications.Commands.MarkAsRead;

public sealed class MarkAsReadValidator : AbstractValidator<MarkAsReadCommand>
{
    public MarkAsReadValidator() => RuleFor(x => x.Id).NotEmpty();
}
