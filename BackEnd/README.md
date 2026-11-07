# MyCloud — Backend

.NET 10 microservices. Kiến trúc & kế hoạch: [../docs/](../docs/README.md).

## Yêu cầu

- .NET SDK 10 (xem `global.json`)
- Docker Desktop (đang chạy)

## Chạy toàn bộ hệ thống (khuyến nghị)

```bash
dotnet run --project src/Aspire/MyCloud.AppHost
```

Aspire khởi động PostgreSQL, MongoDB, RabbitMQ, Redis, MinIO, Mailpit và tất cả service; console in ra URL dashboard (log, trace, metric). Lần đầu chạy sẽ mất vài phút để kéo image. Container được giữ lại giữa các lần chạy (`ContainerLifetime.Persistent`).

| Thành phần | URL (dev) |
|---|---|
| Aspire dashboard | http://localhost:15080 |
| Gateway | http://localhost:5000 — `/api/{identity\|drive\|storage\|sharing\|search\|notification}/v1/...` |
| Identity | http://localhost:5101/scalar |
| Drive | http://localhost:5102/scalar |
| Storage / Sharing (khung) | http://localhost:5103, http://localhost:5104 |
| Search | http://localhost:5105/scalar |
| Notification (khung) | http://localhost:5106 |

Khoá ký JWT tạm thời (Phase 0) được Aspire sinh tự động và lưu trong user-secrets của AppHost — không có secret nào trong `appsettings*.json`.

Ví dụ qua gateway:

```bash
curl -X POST http://localhost:5000/api/identity/v1/auth/register -H "Content-Type: application/json" -d '{"email":"a@example.com","password":"Passw0rd!","displayName":"A"}'
```

```bash
curl -X POST http://localhost:5000/api/drive/v1/folders -H "Content-Type: application/json" -d '{"name":"Tài liệu"}'
```

```bash
curl http://localhost:5000/api/search/v1/folders
```

## Cấu trúc

```
src/
  Aspire/        MyCloud.AppHost (orchestration dev), MyCloud.ServiceDefaults (OTel, health, resilience)
  BuildingBlocks/ Domain | Application (MediatR pipeline) | Messaging (MassTransit + outbox) | Grpc | Web
  Contracts/     MyCloud.Contracts.Events (integration event) | Protos (gRPC, buf)
  Gateway/       MyCloud.Gateway (YARP)
  Services/      Identity | Drive (4 lớp) | Search | Storage | Sharing | Notification | Media (worker)
tests/           Drive.Domain.Tests | Drive.IntegrationTests (Testcontainers) | Architecture.Tests
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
