# Chạy TOEICSpace LMS bằng Docker

Hướng dẫn này dành cho môi trường phát triển local. Docker Compose chạy API Gateway, năm service Identity, Classroom, Assessment, Content, Payment, năm database MySQL, RabbitMQ và Redis. Frontend chạy riêng bằng Vite.

## 1. Chuẩn bị

- Cài và khởi động Docker Desktop với Docker Compose v2. Trên Linux có thể dùng Docker Engine và Compose plugin. Kiểm tra bằng `docker compose version` và `docker info`.
- Cài .NET SDK 10 trên máy nếu cần chạy script tạo khóa JWT. Các API và migration dùng .NET trong container.
- Nếu chạy frontend, cài Node.js và npm theo yêu cầu của repo frontend.

Mở terminal tại thư mục gốc `toeic-space-lms-be`. Nếu chưa có file cấu hình local, tạo từ mẫu:

```bash
test -f .env || cp .env.example .env
```

Điền các giá trị trong `.env` của backend và giữ file này ngoài Git. Nếu đã có database trong volume LMS, dùng lại tên database, user và mật khẩu đã đặt lần đầu. Ví dụ, một volume Identity đã khởi tạo với `toeic_space_identity` có thể tiếp tục dùng tên đó. Mẫu hiện dùng `toeic_space_lms_identity` cho lần khởi tạo mới.

## 2. Cấu hình backend

| Nhóm | Cấu hình cần kiểm tra |
|---|---|
| Môi trường local | `ASPNETCORE_ENVIRONMENT=Development`, `API_GATEWAY_PORT=15050`. Script sử dụng Compose project `toeic-space-lms-be`. |
| MySQL | Với từng tiền tố `IDENTITY`, `CLASSROOM`, `ASSESSMENT`, `CONTENT`, `PAYMENT`, điền đủ `*_DB_NAME`, `*_DB_USER`, `*_DB_PASSWORD`, `*_DB_ROOT_PASSWORD`, `*_DB_PORT`. |
| RabbitMQ và Redis | Điền `RABBITMQ_USER`, `RABBITMQ_PASSWORD` và giữ các cổng từ mẫu nếu còn trống. Nếu project cũ dùng cổng host `15672` cho RabbitMQ Management, lấy một cổng trống khác cho `RABBITMQ_PORT`, ví dụ `15673`. |
| JWT | Giữ cặp `JWT_ISSUER` và `JWT_AUDIENCE` của LMS. Điền `JWT_PRIVATE_KEY` và `JWT_PUBLIC_KEY` từ **cùng một lần** tạo khóa. |
| OTP | Điền `IDENTITY_OTP_HMAC_SECRET` bằng chuỗi ngẫu nhiên, chẳng hạn đầu ra của `openssl rand -hex 32`. Lưu để dùng lại. |
| Admin đầu tiên | Điền `BOOTSTRAP_ADMIN_EMAIL` và `BOOTSTRAP_ADMIN_PASSWORD`. Mật khẩu tối thiểu 12 ký tự và có ít nhất ba nhóm trong chữ thường, chữ hoa, chữ số và ký tự đặc biệt. Nếu email đã tồn tại, bootstrap giữ mật khẩu hiện có. |
| Proxy tin cậy | Với local, có thể để trống `TRUSTED_PROXY_NETWORK`. Nếu cấu hình, dùng CIDR hợp lệ của mạng proxy tin cậy. |

Để tạo cặp JWT cho lần đầu, chạy lệnh sau tại repo backend rồi đưa hai dòng `JWT_PRIVATE_KEY=...` và `JWT_PUBLIC_KEY=...` vào `.env` theo cách riêng tư. Giữ lại cặp khóa cho những lần chạy sau vì đổi khóa làm access token cũ mất hiệu lực.

```bash
dotnet run scripts/GenerateJwtKeys.cs
```

Cổng MySQL trong `.env` là cổng trên máy host. Các API và migration trong Compose dùng tên service như `identity-db:3306` trên mạng Docker.

## 3. Cấu hình các tích hợp cần dùng

| Tính năng | Biến trong `.env` |
|---|---|
| Xác thực email, gửi lại OTP và đặt lại mật khẩu | `SMTP_SERVER`, `SMTP_PORT`, `SMTP_SENDER_EMAIL`, `SMTP_PASSWORD`, `SMTP_SENDER_NAME`, `SMTP_SUPPORT_EMAIL`. Identity dùng địa chỉ gửi để đăng nhập SMTP. Dùng cổng STARTTLS của nhà cung cấp, thường là 587. |
| Upload avatar và nội dung | `R2_ENDPOINT`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`, `R2_BUCKET_NAME`. Dùng bucket và tài khoản thử nghiệm. |
| Google Meet | `GOOGLE_MEET_CLIENT_ID`, `GOOGLE_MEET_CLIENT_SECRET`, `GOOGLE_MEET_REDIRECT_URI`, `GOOGLE_MEET_ENCRYPTION_KEY`. URI callback phải khớp với cấu hình OAuth. Giữ khóa mã hóa nếu cần đọc lại thông tin kết nối đã lưu. |
| SePay | `SEPAY_BANK`, `SEPAY_ACCOUNT_NUMBER`, `SEPAY_WEBHOOK_API_KEY`. Đường dẫn webhook qua Gateway là `/payment/api/v1/webhooks/sepay`. Để SePay gọi được từ bên ngoài, dùng URL môi trường thử nghiệm mà SePay truy cập được. |

Compose đọc `.env` khi truyền `--env-file .env`. Container chỉ nhận các biến được khai báo cho service đó trong `docker-compose.yml`. Biến đã export trong terminal được ưu tiên hơn giá trị cùng tên trong file. Khi thêm một biến mới theo quy ước hiện tại, thêm mapping vào đúng service trong Compose.

## 4. Khởi động và kiểm tra

```bash
./scripts/start-lms.sh --env-file .env
```

Script kiểm tra cấu hình Compose, chờ năm MySQL sẵn sàng, áp dụng EF migrations cho cả năm service rồi build và khởi động các API. Nếu migration thất bại, script dừng trước bước khởi động API. Mỗi lần chạy lại, EF áp dụng các migration còn thiếu. Nếu muốn dùng các biến đã export, chạy `./scripts/start-lms.sh` không kèm cờ.

```bash
docker compose --env-file .env -p toeic-space-lms-be --profile full ps -a
docker compose --env-file .env -p toeic-space-lms-be port api-gateway 8080
curl --max-time 5 -sS -o /dev/null -w 'HTTP %{http_code}\n' \
  -H 'Content-Type: application/json' -d '{}' \
  http://127.0.0.1:15050/identity/api/auth/login
```

Nếu lệnh `port` trả cổng khác `15050`, thay cổng trong lệnh `curl` bằng cổng thực tế. Với body `{}`, HTTP 400 là kết quả mong đợi vì thiếu dữ liệu đăng nhập. Đăng nhập bằng tài khoản Admin từ bước cấu hình để kiểm tra luồng đầy đủ.

Trong repo `toeic-space-lms-fe`, chạy:

```bash
npm ci
VITE_LMS_GATEWAY=http://127.0.0.1:15050 npm run dev
```

Mở địa chỉ `Local` mà Vite in ra, mặc định là `http://localhost:15173`. Có thể đặt `VITE_LMS_GATEWAY` trong `.env` **của frontend**. Khởi động lại Vite sau khi đổi cấu hình.

## 5. Xử lý lỗi thường gặp

| Triệu chứng | Cách kiểm tra |
|---|---|
| `ECONNREFUSED 127.0.0.1:15050` | Kiểm tra trạng thái Gateway và cổng publish bằng lệnh `port`. Đồng bộ cổng với `VITE_LMS_GATEWAY`. |
| Gateway trả `502` | Xem log Gateway và service phía sau. Với đăng nhập, kiểm tra Identity có báo `Now listening on ...:8080` hay dừng vì JWT, mật khẩu Admin, CIDR hoặc schema MySQL. |
| MySQL `unhealthy`, `Access denied` hoặc `Unknown database` | So sánh thông tin cấu hình với lúc volume được tạo. Đổi biến MySQL sau khi khởi tạo volume không tự tạo database mới hay đổi mật khẩu trong MySQL. Giữ volume để bảo toàn dữ liệu. |
| `Table ... Users doesn't exist` | Kiểm tra migration Identity có thành công trên đúng database mà API đang dùng. Chạy lại script với cùng cấu hình cho database, migration và API. |
| Không nhận được mã email | Kiểm tra SMTP trong `.env`, tạo lại Identity và bấm **Gửi lại mã**. Xem log `SmtpException` hoặc `Sent an email verification message`. Nếu SMTP đã nhận thư, kiểm tra địa chỉ nhận và thư rác. |
| Không bind được cổng | Kiểm tra Docker Desktop và các tiến trình local đang dùng cổng. Đổi cổng host trong `.env`. Với Gateway, cập nhật đồng thời target của Vite. |

```bash
docker compose --env-file .env -p toeic-space-lms-be \
  logs --tail 80 identity-api api-gateway

# Ví dụ sau khi đổi riêng cấu hình SMTP
docker compose --env-file .env -p toeic-space-lms-be --profile full \
  up -d --no-deps --force-recreate identity-api
```

## 6. Dừng hoặc chạy lại

Dừng container và giữ dữ liệu local:

```bash
docker compose --env-file .env -p toeic-space-lms-be --profile full stop
```

Khi cần chạy lại sau khi sửa code hoặc cập nhật migration, dùng `./scripts/start-lms.sh --env-file .env`. Tránh dùng `down -v` khi muốn giữ dữ liệu MySQL vì tùy chọn `-v` xóa volume của project.

## Nạp dữ liệu demo Đà Nẵng

Sau khi stack Docker LMS đã khởi động và migration thành công, chạy từ thư mục gốc bằng Python 3:

```sh
python3 scripts/seed_lms_danang.py
```

Nhập một mật khẩu demo dài ít nhất 12 ký tự khi được hỏi. Mật khẩu dùng chung cho các tài khoản mẫu được tạo trong lần chạy đó. Dùng `admin@danang.example` cho cổng Admin, `teacher1@danang.example` đến `teacher4@danang.example` cho cổng giáo viên và `student1@danang.example` đến `student48@danang.example` cho cổng học viên.

Seed chỉ ghi vào Identity và Classroom. Bốn cơ sở ở Hải Châu, Sơn Trà, Thanh Khê và Ngũ Hành Sơn có bốn lớp TOEIC 550+, 750+, 650+ và 850+ tương ứng. Mỗi lớp có một phòng, một giáo viên, 12 học viên và bốn buổi học thứ Hai từ 18:00 đến 19:30. Khi tạo mới, lịch bắt đầu vào thứ Hai tuần trước theo giờ Đà Nẵng. Buổi đầu mỗi lớp đã có điểm danh.

Seed dùng Docker project `toeic-space-lms-be` đang chạy. Mỗi database được ghi trong một transaction riêng. Nếu một service báo lỗi, sửa nguyên nhân rồi chạy lại cùng lệnh. Bản ghi demo đã tồn tại giữ nguyên ID và mật khẩu.
