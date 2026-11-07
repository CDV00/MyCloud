# MyCloud — Backend

.NET 10 microservices. Kiến trúc & kế hoạch: [../docs/](../docs/README.md).

## Yêu cầu

- .NET SDK 10 (xem `global.json`)
- Docker Desktop (đang chạy)
- Node.js ≥ 20.18 cho web app (khuyến nghị Node 24 LTS)

## Chạy toàn bộ hệ thống (khuyến nghị)

```bash
dotnet run --project src/Aspire/MyCloud.AppHost
```

Aspire khởi động PostgreSQL, MongoDB, RabbitMQ, Redis, MinIO, Mailpit, tất cả service và web app (Vite); console in ra URL dashboard (log, trace, metric). Lần đầu chạy sẽ mất vài phút để kéo image và `npm install`. Container được giữ lại giữa các lần chạy (`ContainerLifetime.Persistent`).

| Thành phần | URL (dev) |
|---|---|
| **Web app** | http://localhost:5173 |
| Aspire dashboard | http://localhost:15080 |
| Gateway | http://localhost:5000 — `/api/{identity\|drive\|storage\|sharing\|search\|notification}/v1/...` |
| Identity | http://localhost:5101/scalar — JWKS: `/.well-known/jwks.json` |
| Drive | http://localhost:5102/scalar |
| Storage | http://localhost:5103/scalar (REST) — gRPC nội bộ ở endpoint `grpc` (HTTP/2) |
| Sharing (khung) | http://localhost:5104 |
| Search | http://localhost:5105/scalar |
| Notification (khung) | http://localhost:5106 |
| RabbitMQ management | http://localhost:15672 (mật khẩu: Aspire dashboard → rabbitmq → Parameters) |
| Mailpit | http://localhost:8025 |
| MinIO console | http://localhost:9001 (API S3: http://localhost:9000, proxy cùng origin tại `/mycloud-blobs/`) |

Nếu đang chạy nginx gateway (`D:\Docker\nginx`), app còn mở được ở **http://mycloud.localhost**, và **http://localhost/** là menu quản lý có trạng thái của mọi app.

### Xác thực

Mọi API (trừ `/api/identity/v1/auth/*` và health check) yêu cầu header `Authorization: Bearer <access token>`; thiếu hoặc sai token ⇒ 401 ở Gateway và cả ở service. Identity ký token bằng RSA (RS256), khoá tự sinh và tự xoay vòng — không có secret nào trong `appsettings*.json` (ADR-012). Dữ liệu Drive/Search chỉ hiển thị với chủ sở hữu.

Ví dụ qua Gateway (client không phải trình duyệt nhận refresh token trong body):

```bash
curl -X POST http://localhost:5000/api/identity/v1/auth/register -H "Content-Type: application/json" -d '{"email":"a@example.com","password":"Passw0rd!","displayName":"A"}'
```

```bash
curl -X POST http://localhost:5000/api/identity/v1/auth/login -H "Content-Type: application/json" -d '{"email":"a@example.com","password":"Passw0rd!","refreshTokenInBody":true}'
```

```bash
curl -X POST http://localhost:5000/api/drive/v1/folders -H "Authorization: Bearer <accessToken>" -H "Content-Type: application/json" -d '{"parentId":"<rootId>","name":"Tài liệu"}'
```

```bash
curl http://localhost:5000/api/drive/v1/items/root -H "Authorization: Bearer <accessToken>"
```

Upload: `POST /api/drive/v1/uploads` trả URL đã ký cho từng part → PUT từng part thẳng lên object storage → `POST /api/drive/v1/uploads/{id}/complete` với danh sách ETag (docs/05). Web app làm sẵn việc này.

Trên Scalar (`/scalar` của từng service), bấm **Authentication → Bearer** và dán access token.

Giới hạn tần suất ở Gateway (cấu hình `RateLimiting` trong `src/Gateway/MyCloud.Gateway/appsettings.json`): đăng nhập/đăng ký 5 lần/phút/IP, refresh/logout 30 lần/phút/IP, API 100 request/giây/user.

## Cấu trúc

```
src/
  Aspire/        MyCloud.AppHost (orchestration dev), MyCloud.ServiceDefaults (OTel, health, resilience)
  BuildingBlocks/ Domain | Application (MediatR pipeline) | Messaging (MassTransit + outbox) | Grpc
                 | Authentication (JWT RS256 qua JWKS) | Web (ProblemDetails, versioning, OpenAPI, Carter)
  Contracts/     MyCloud.Contracts.Events (integration event) | Protos (gRPC, buf)
  Gateway/       MyCloud.Gateway (YARP, xác thực, rate limit, correlation id)
  Services/      Identity | Drive (4 lớp) | Search | Storage | Sharing | Notification | Media (worker)
tests/           Drive.Domain.Tests | Drive.IntegrationTests | Identity.IntegrationTests | Storage.IntegrationTests
                 (Testcontainers: PostgreSQL, MinIO) | Gateway.Tests | Architecture.Tests | MyCloud.Testing (dùng chung)
```

## Lệnh thường dùng

```bash
dotnet build MyCloud.slnx
```

```bash
dotnet test MyCloud.slnx
```

Thêm migration (ví dụ Drive):

```bash
dotnet ef migrations add <Name> --project src/Services/Drive/Drive.Infrastructure --output-dir Persistence/Migrations
```

`dotnet-ef` là local tool (`dotnet tool restore`). Ở môi trường Development, service tự áp dụng migration khi khởi động.

## Không dùng Aspire

```bash
docker compose -f ../deploy/docker-compose.yml up -d
```

Sau đó cấu hình connection string cho từng service bằng user-secrets, ví dụ cho Drive:

```bash
dotnet user-secrets --project src/Services/Drive/Drive.Api set "ConnectionStrings:drivedb" "Host=localhost;Database=drive;Username=mycloud;Password=mycloud-dev"
```

```bash
dotnet user-secrets --project src/Services/Drive/Drive.Api set "ConnectionStrings:rabbitmq" "amqp://mycloud:mycloud-dev@localhost:5672"
```
