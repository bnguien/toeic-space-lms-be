using MassTransit;
using MediatR;
using ToeicSpace.BuildingBlocks.Messaging.Events;
using ToeicSpace.Notification.Application.Features.Notifications.Commands.CreateNotification;

namespace ToeicSpace.Notification.Infrastructure.Consumers;

public sealed class LmsActivityConsumer(ISender sender) : IConsumer<LmsActivityIntegrationEvent>
{
    public Task Consume(ConsumeContext<LmsActivityIntegrationEvent> context)
        => sender.Send(Map(context.Message), context.CancellationToken);

    public static CreateNotificationCommand Map(LmsActivityIntegrationEvent message)
    {
        var (source, title) = message.Activity switch
        {
            LmsActivity.StudentEnrolled => ("Classroom", "Bạn đã được thêm vào lớp"),
            LmsActivity.StudentWithdrawn => ("Classroom", "Bạn đã được rút khỏi lớp"),
            LmsActivity.TeacherAssigned => ("Classroom", "Bạn được phân công phụ trách lớp"),
            LmsActivity.TeacherUnassigned => ("Classroom", "Bạn không còn phụ trách lớp"),
            LmsActivity.ClassTeacherChanged => ("Classroom", "Giáo viên phụ trách lớp đã thay đổi"),
            LmsActivity.ClassStarted => ("Classroom", "Lớp học đã bắt đầu"),
            LmsActivity.ClassCompleted => ("Classroom", "Lớp học đã kết thúc"),
            LmsActivity.ScheduleCreated => ("Classroom", "Lớp có lịch học mới"),
            LmsActivity.SessionAdded => ("Classroom", "Lớp có buổi học mới"),
            LmsActivity.SessionUpdated => ("Classroom", "Thời gian hoặc phòng học đã thay đổi"),
            LmsActivity.SessionCancelled => ("Classroom", "Buổi học đã bị hủy"),
            LmsActivity.MeetChanged => ("Classroom", "Liên kết Google Meet đã thay đổi"),
            LmsActivity.AttendanceAbsent => ("Classroom", "Bạn được ghi nhận vắng mặt"),
            LmsActivity.AttendanceAdjusted => ("Classroom", "Kết quả điểm danh của bạn đã được cập nhật"),
            LmsActivity.ClassUpdated => ("Classroom", "Thông tin lớp học đã thay đổi"),
            LmsActivity.StudentsJoined => ("Classroom", "Lớp của bạn có học viên mới"),
            LmsActivity.AccountActivated => ("Identity", "Tài khoản của bạn đã được kích hoạt"),
            LmsActivity.AccountSuspended => ("Identity", "Tài khoản của bạn đã bị khóa"),
            LmsActivity.AccountUnlocked => ("Identity", "Tài khoản của bạn đã được mở khóa"),
            LmsActivity.PasswordChanged => ("Identity", "Mật khẩu của bạn đã được thay đổi"),
            LmsActivity.RoleChanged => ("Identity", "Quyền truy cập của bạn đã được cập nhật"),
            LmsActivity.TuitionCreated => ("Payment", "Bạn có khoản học phí mới"),
            LmsActivity.TuitionPaymentRecorded => ("Payment", "Đã ghi nhận thanh toán học phí"),
            LmsActivity.TuitionWaived => ("Payment", "Bạn được miễn giảm học phí"),
            LmsActivity.ReconciliationRequired => ("Payment", "Có giao dịch cần đối soát"),
            LmsActivity.GrammarCommentReplied => ("Content", "Có người trả lời bình luận Grammar của bạn"),
            _ => throw new ArgumentOutOfRangeException(nameof(message.Activity))
        };
        var body = string.IsNullOrWhiteSpace(message.Detail) ? title : $"{title}. {message.Detail}";
        return new(message.RecipientIds, title, body, message.Activity.ToString(), source, message.Id,
            message.RelatedEntityId, null);
    }
}

public sealed class LmsActivityConsumerDefinition : ConsumerDefinition<LmsActivityConsumer>
{
    public LmsActivityConsumerDefinition() => EndpointName = "notification-lms-activity";
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpoint,
        IConsumerConfigurator<LmsActivityConsumer> consumer, IRegistrationContext context) =>
        NotificationConsumerRetry.Configure(endpoint);
}
