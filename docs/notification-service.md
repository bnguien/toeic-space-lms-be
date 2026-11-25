# Notification Service

Notification Service lưu hộp thông báo cá nhân của Admin, Teacher và Student. Identity cấp JWT, RabbitMQ chuyển sự kiện nghiệp vụ, MySQL lưu trạng thái và SignalR cập nhật giao diện khi người dùng đang kết nối.

## Kiến trúc và mã nguồn

| Project | Trách nhiệm |
|---|---|
| `ToeicSpace.Notification.Domain` | Entity Notification với dữ liệu nội dung, nguồn, người nhận và thời điểm đọc |
| `ToeicSpace.Notification.Application` | Sáu vertical slice dùng MediatR, FluentValidation, DTO và interface lưu trữ/phân phối |
| `ToeicSpace.Notification.Infrastructure` | Consumer MassTransit, repository, EF Core DbContext, migration và nhận diện lỗi duplicate key của MySQL |
| `ToeicSpace.Notification.API` | Controller, OpenAPI, Problem Details, JWT, health check, Hub và implementation SignalR |

Application tham chiếu Domain và dùng `PaginatedResult<T>` từ BuildingBlocks. Infrastructure tham chiếu Application và BuildingBlocks.Messaging. API kết hợp Application và Infrastructure, dùng ES256 JWT từ BuildingBlocks.Security. CQRS và validation theo convention MediatR của Classroom và Identity. Các thư mục CQRS và Behaviors của BuildingBlocks chưa có implementation dùng chung. Repository cam kết mỗi insert bằng `SaveChangesAsync` và dùng một câu lệnh SQL có điều kiện để cập nhật trạng thái đọc.

## MySQL

Provider là Pomelo.EntityFrameworkCore.MySql 9.0.0 với EF Core 9.0.11, khớp với Classroom và Assessment. Compose dùng MySQL 8.4 riêng tại `notification-db`, volume `notification_db_data`. Schema mặc định là `toeic_space_lms_notification`, với account và connection string riêng. Migration `InitialNotification` tạo bảng `Notifications` bằng `utf8mb4` và collation `utf8mb4_0900_ai_ci`.

| Cột | Kiểu dữ liệu và ý nghĩa |
|---|---|
| `Id`, `RecipientId`, `SourceEventId` | `char(36)`, GUID thông báo, chủ sở hữu và sự kiện |
| `Title`, `Body` | `varchar(200)` và `varchar(2000)` |
| `Type`, `Source` | `varchar(80)`, mã loại và tên nguồn |
| `RelatedEntityId`, `NavigationPath` | GUID và đường dẫn nội bộ `varchar(512)`, đều tùy chọn |
| `CreatedAt`, `ReadAt` | `datetime(6)`, UTC. `ReadAt = NULL` nghĩa là chưa đọc |

Khóa chính là `Id`. Các index là `(RecipientId, CreatedAt DESC, Id DESC)` cho danh sách và `(RecipientId, ReadAt, CreatedAt DESC, Id DESC)` cho truy vấn theo trạng thái. Unique index `(Source, SourceEventId, RecipientId, Type)` trong migration `IncludeNotificationTypeInEventKey` cho phép mỗi người nhận có một thông báo cho từng loại của cùng sự kiện. Consumer sử dụng mã Type cố định khi phát lại. Retry sau khi xử lý một phần chỉ bổ sung các khóa còn thiếu. Service không dùng khóa ngoại hay truy vấn đến schema của service khác.

## Sự kiện và phân phối

| Nguồn và contract | Consumer queue | Thông báo |
|---|---|---|
| Assessment · `ExamAttemptSubmittedIntegrationEvent` | `notification-exam-attempt-submitted` | `ExamAttemptScored` cho người nộp bài thi FullTest đã chấm. `relatedEntityId` là attempt ID |
| Identity · `ActiveAccountCreatedIntegrationEvent` | `notification-active-account-created` | `ActiveAccountCreated` cho tài khoản được tạo ở trạng thái Active, bao gồm tài khoản do quản trị viên cấp. `relatedEntityId` là user ID |

Assessment tiếp tục sử dụng outbox hiện có. Identity ghi contract mới vào outbox trong cùng lần lưu tài khoản, với event ID bằng user ID. Payload chỉ chứa user ID và thời điểm phát sinh. Các sự kiện đồng bộ projection `StudentChanged` và `TeacherChanged` có thể phát lại toàn bộ dữ liệu, nên không dùng làm trigger tạo thông báo.

Consumer ánh xạ sự kiện sang `CreateNotificationCommand`. Handler loại bỏ người nhận lặp trong payload, lưu riêng từng người nhận, sau đó mới gửi SignalR. Nếu một lần xử lý bị gián đoạn giữa danh sách người nhận, RabbitMQ có thể giao lại và unique index giúp lưu tiếp các bản ghi còn thiếu. MassTransit retry tối đa năm lần sau lần xử lý đầu, cách nhau một giây. Các lỗi validation đi thẳng vào queue `<tên-queue>_error`. Lỗi lưu trữ sau khi hết retry cũng vào queue này. Sau khi khắc phục nguyên nhân, có thể phát lại message với event ID gốc.

SignalR gửi theo cơ chế best effort. Khi tab offline hoặc transport gặp lỗi, dữ liệu đã cam kết vẫn đọc được qua REST. Duplicate delivery không phát lại thông báo đã lưu. Nếu tiến trình dừng sau khi commit và trước khi gửi SignalR, frontend đồng bộ lại qua API. Compose mặc định chạy một replica có kết nối Redis. Khi có nhiều replica, MassTransit giao sự kiện cho consumer, handler lưu vào MySQL rồi gọi SignalR theo user ID trong JWT. Redis Pub/Sub chuyển tiếp tới các replica đang giữ kết nối của người nhận, gồm cả các tab trên thiết bị khác.

### Cấu hình và chạy nhiều replica

| Biến Compose | Cấu hình API | Ý nghĩa |
|---|---|---|
| `NOTIFICATION_SIGNALR_MODE` | `SignalR__Mode` | `Redis` trong Compose. Chạy local với đúng một tiến trình có thể chọn `SingleInstance` (mặc định khi chạy ngoài Compose). |
| `NOTIFICATION_REDIS_CONNECTION` | `ConnectionStrings__Redis` | Mặc định `redis:6379`, dùng Redis chung của LMS. Mọi replica phải cùng endpoint. |
| `NOTIFICATION_SIGNALR_PREFIX` | `SignalR__ChannelPrefix` | Compose dùng tên project kèm hậu tố `-notification`. Đặt riêng cho từng deployment dùng chung Redis và giống nhau trên mọi replica cùng deployment. |

Sau khi provision và migrate như hướng dẫn bên dưới, chạy một replica bằng Compose cơ sở. Để chạy hai replica:

```bash
NOTIFICATION_REPLICA_B=true docker compose --profile notification --profile notification-scale up -d --build redis notification-api notification-api-b api-gateway
```

Dùng Redis mode cho cả hai API. Profile notification-scale bật replica B; NOTIFICATION_REPLICA_B=true đăng ký B với Gateway và bật cookie affinity. Khi trở về một replica, chạy lại Gateway với biến này bằng false. Client giữ cookie từ bước negotiate đến khi mở transport; khi gọi khác origin cần cho phép credentials. Mỗi replica dùng cổng nội bộ 8080. Khi thêm replica thứ ba, đăng ký thêm destination có địa chỉ riêng trong YARP để affinity nhận diện chính xác từng tiến trình.

Redis mode yêu cầu connection string và prefix. Khi Redis gián đoạn, health check Redis báo `Degraded`; REST vẫn phục vụ dữ liệu MySQL khi database sẵn sàng. SignalR ghi log lỗi gửi và tự kết nối lại. Pub/Sub không lưu sự kiện để phát bù. Frontend tải lại danh sách và unread count khi reconnect hoặc mở hộp thông báo, kể cả khi WebSocket vẫn mở suốt thời gian Redis gián đoạn.

Job CI `notification-integration` chạy hai host ASP.NET Core với Redis 7, RabbitMQ và MySQL thật. Để chạy ở local, thêm `NOTIFICATION_TEST_REDIS` vào các biến integration test bên dưới và chạy cùng lệnh test. Riêng ca ngắt và khôi phục Redis cần `NOTIFICATION_TEST_REDIS_CONTAINER` là ID container Redis **dành riêng cho test**. Test dừng rồi khởi động lại container này.

### Sự kiện nghiệp vụ LMS

Các producer dưới đây ghi sự kiện cùng giao dịch với thay đổi nghiệp vụ. Classroom, Payment và Content có bảng riêng `LmsOutboxMessages`, index trên `ProcessedOn, OccurredOn`. Áp dụng migration `AddLmsActivityOutbox` ở cả ba database trước khi chạy phiên bản mới. Identity dùng bảng outbox hiện hữu. Consumer `LmsActivityConsumer` nhận `LmsActivityIntegrationEvent` qua queue `notification-lms-activity`. Tạo binding trước khi bật producer. Dispatcher giữ nguyên ID qua các lần publish lại. Với ba outbox mới, bản ghi chưa xử lý đạt 50 lần lỗi cần được vận hành kiểm tra và đặt lại Attempts sau khi xử lý nguyên nhân.

| Service và producer | Type trả cho Frontend | Người nhận | relatedEntityId |
|---|---|---|---|
| Classroom · ghi danh đơn, bulk, import | StudentEnrolled, StudentsJoined | Từng học viên mới và một thông báo cho giáo viên trong mỗi thao tác | Lớp |
| Classroom · rút lớp | StudentWithdrawn | Học viên vừa rút | Lớp |
| Classroom · phân công | TeacherAssigned, TeacherUnassigned, ClassTeacherChanged | Giáo viên mới, giáo viên cũ, học viên đang theo lớp khi phân công giáo viên mới | Lớp |
| Classroom · vòng đời và sửa lớp | ClassStarted, ClassCompleted, ClassUpdated | Học viên đang theo lớp và giáo viên | Lớp |
| Classroom · lịch định kỳ | ScheduleCreated | Học viên và giáo viên | Lịch |
| Classroom · tạo riêng, đổi thời gian/phòng, hủy, đổi Meet | SessionAdded, SessionUpdated, SessionCancelled, MeetChanged | Học viên và giáo viên phụ trách | Buổi học |
| Classroom · audit điểm danh | AttendanceAbsent, AttendanceAdjusted | Học viên liên quan | Buổi học |
| Identity · chuyển trạng thái, đổi vai trò, đổi/reset mật khẩu | AccountActivated, AccountSuspended, AccountUnlocked, RoleChanged, PasswordChanged | Chủ tài khoản | Tài khoản |
| Payment · tạo khoản thu, SePay/tiền mặt, miễn giảm | TuitionCreated, TuitionPaymentRecorded, TuitionWaived | Học viên đứng tên khoản thu | Khoản thu |
| Payment · giao dịch Unmatched, Ambiguous, Overpaid | ReconciliationRequired | Admin đang hoạt động do Identity xác định qua PaymentReconciliationRequested | Giao dịch |
| Content · CreateComment | GrammarCommentReplied | Tác giả bình luận được trả lời, loại trừ tự trả lời | null |

Frontend dùng `source`, `type` và `relatedEntityId` để xác định đối tượng. Các loại mới hiện trả `navigationPath = null`. Hiển thị nội dung khi chưa có màn hình đích phù hợp. Trạng thái thanh toán một phần hoặc tất toán nằm trong nội dung thông báo. Sau reconnect, tải lại danh sách và số chưa đọc qua REST.

Ngoài phạm vi đợt này gồm chuyển/hủy lớp, xin nghỉ, học liệu và bài tập theo lớp, hạn phí, hoàn tiền, hóa đơn, thông báo chung, nhắc lịch và chat. Hai luồng ActiveAccountCreated và FullTest dùng các contract và queue riêng ở trên.

## API và tích hợp frontend

Base path qua Gateway là `/notification/api/v1/notifications`. Khi chạy trực tiếp vào Notification API, bỏ đoạn `/notification`. Dùng access token Identity trong header `Authorization: Bearer <token>`. Student hiện nhận role `User` từ Identity. Người dùng thuộc các role hợp lệ đều bị giới hạn bởi `sub` trong JWT, bao gồm Admin.

| Method | Path tương đối | Kết quả |
|---|---|---|
| GET | `/` | `{ items, pageIndex, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }` |
| GET | `/unread-count` | `{ count }` |
| GET | `/{id}` | Notification DTO hoặc 404 |
| PATCH | `/{id}/read` | 204 nếu thuộc người dùng, kể cả khi đã đọc trước đó. 404 nếu không tìm thấy trong hộp thông báo của người dùng |
| PATCH | `/read-all` | 204 sau khi đánh dấu các thông báo chưa đọc của người dùng hiện tại |

Danh sách hỗ trợ `pageIndex` từ 1, `pageSize` mặc định 20 và tối đa 100, `isRead=true|false` và `type`. Sắp xếp theo `createdAt` và `id` giảm dần. Notification DTO gồm `id`, `title`, `body`, `type`, `source`, `relatedEntityId`, `navigationPath`, `createdAt`, `readAt`, `isRead`. Các thời điểm trả về theo UTC. Validation trả HTTP 400 với Problem Details. Phản hồi 401 biểu thị thiếu JWT hợp lệ và 403 biểu thị thiếu role hoặc subject hợp lệ.

Hub qua Gateway là `/notification/hubs/notifications`. Sử dụng client SignalR và cung cấp access token mới nhất bằng `accessTokenFactory`. Hub nhận danh tính từ JWT và dùng user ID chuẩn hóa để gửi tới mọi tab hoặc thiết bị kết nối của người dùng. Kết nối đóng khi token dùng để xác thực hết hạn. Khi chạy khác origin, khai báo origin của frontend qua cấu hình Gateway `Cors__AllowedOrigins__0` và các chỉ số kế tiếp.

| Sự kiện Hub | Payload | Xử lý frontend |
|---|---|---|
| `NotificationCreated` | Notification DTO | Cập nhật danh sách theo `id` |
| `UnreadCountChanged` | `{ count }` | Cập nhật badge và làm mới trang đang mở nếu cần đồng bộ trạng thái đọc giữa nhiều tab |

Đăng ký listener trước khi kết nối. Khi kết nối lần đầu hoặc `onreconnected`, gọi lại danh sách và unread count. Sự kiện có thể đến gần đồng thời, nên dùng REST để đồng bộ badge và trạng thái sau reconnect hoặc khi người dùng mở dropdown. Với `ExamAttemptScored`, frontend dùng `relatedEntityId` để mở trang kết quả đã cấu hình trong ứng dụng và lấy kết quả qua `/assessment/api/v1/attempts/{id}/result`. Các event hiện tại trả `navigationPath: null` vì đường dẫn giao diện chưa được xác nhận.

## Cấu hình, chạy và kiểm thử

| Biến | Dùng ở đâu |
|---|---|
| `NOTIFICATION_DB_NAME`, `NOTIFICATION_DB_USER`, `NOTIFICATION_DB_PASSWORD` | MySQL image khởi tạo schema/account trên volume mới. Password được thiết lập trong cấu hình môi trường của dự án |
| `ConnectionStrings__NotificationDatabase` | API, EF Core tooling. Compose dựng từ các biến trên. Với local, cung cấp connection string riêng |
| `RabbitMQ__Host`, `RabbitMQ__Username`, `RabbitMQ__Password`, `RabbitMQ__VirtualHost` | Cấu hình BuildingBlocks.Messaging. Compose dùng `messagebroker` và credentials RabbitMQ hiện có |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__PublicKey` | Xác thực cùng Identity. API chỉ cần public key |
| `ReverseProxy__Clusters__notification-cluster__Destinations__destination1__Address` | Đích YARP, mặc định local ở `http://localhost:5220/` và Compose ở `http://notification-api:8080/` |

Chạy toàn bộ LMS bằng `./scripts/start-lms.sh --env-file .env` sau khi cấu hình các biến. Script chờ MySQL, chạy migration rồi khởi động các API. API dùng NOTIFICATION_DB_PASSWORD; NOTIFICATION_DB_ROOT_PASSWORD chỉ cấp cho MySQL. Từ host dùng NOTIFICATION_API_PORT (6007) và NOTIFICATION_DB_PORT (3311). Nếu schema cũ trên identity-db đã có dữ liệu, cần chuyển dữ liệu sang notification-db trước khi bật consumer. Nếu dùng Compose trực tiếp với profile `notification`, chạy theo thứ tự sau.

```bash
docker compose --profile notification up -d --wait notification-db messagebroker redis
docker compose --profile notification --profile tools run --rm migrations Notification
docker compose --profile notification up -d --build notification-api api-gateway
curl -f http://localhost:15050/notification/health
```

`/health` truy vấn bảng `Notifications` để kiểm tra cả quyền kết nối lẫn schema. Migrations chạy qua công cụ được gọi chủ động. Swagger có tại `/notification/swagger/index.html` trong môi trường Development. Chạy `dotnet test tests/Services/Notification/ToeicSpace.Notification.Tests` cho bộ test. Test tích hợp cần `NOTIFICATION_TEST_MYSQL` trỏ tới database dành riêng cho test và `NOTIFICATION_TEST_RABBITMQ_HOST` trỏ tới broker test. Nếu broker sử dụng account riêng, đặt thêm `NOTIFICATION_TEST_RABBITMQ_USER` và `NOTIFICATION_TEST_RABBITMQ_PASSWORD`. CI có job chạy MySQL 8.4 và RabbitMQ thật, apply migration, kiểm tra API/SignalR và các lần xử lý đồng thời.

## Mở rộng và chuyển giao

Khi thêm Email hoặc Push, giữ giao dịch lưu hộp thông báo độc lập với transport. Bổ sung kênh bằng handler hoặc background dispatcher theo yêu cầu retry thực tế. Các email xác minh và phục hồi mật khẩu vẫn thuộc Identity.

Để chuyển sang `toeic-space-be`, lấy Domain, Application, persistence/migration và phần API dùng JWT/SignalR. Mang theo dependencies tương thích của BuildingBlocks, rồi chọn các consumer và contract phù hợp với các producer thực tế ở repo đích. Thiết lập database và route riêng tại môi trường đích. Các quy trình nhận sự kiện Identity/Assessment của LMS hiện nằm trong Infrastructure và có thể thay thế riêng.
