# MyCloud

Ứng dụng lưu trữ đám mây kiểu Google Drive — backend microservice .NET 10 (gRPC + RabbitMQ), frontend React.

- **Kiến trúc, đặc tả, kế hoạch**: [docs/](docs/README.md)
- **Trạng thái**: Phase 0 (tái cấu trúc & hạ tầng) xong — tiếp theo Phase 1 (Identity, Gateway, Web nền). Xem [kế hoạch](docs/07-ke-hoach-thuc-hien.md).

## Cấu trúc

```
BackEnd/    .NET 10 — MyCloud.slnx: Aspire AppHost, Gateway (YARP), Identity, Drive, Search,
            Storage/Sharing/Notification/Media (khung), BuildingBlocks, Contracts, tests
FrontEnd/   my-drive: React 18 (Create React App) — sẽ chuyển sang Vite + TypeScript ở Phase 1
deploy/     docker-compose cho hạ tầng (khi không dùng Aspire)
docs/       tài liệu kiến trúc, ADR, kế hoạch
```

## Chạy nhanh

Yêu cầu: .NET SDK 10, Docker Desktop.

```bash
dotnet run --project BackEnd/src/Aspire/MyCloud.AppHost
```

Lệnh trên khởi động toàn bộ hạ tầng (PostgreSQL, MongoDB, RabbitMQ, Redis, MinIO, Mailpit) và các service, kèm dashboard theo dõi log/trace. Chi tiết: [BackEnd/README.md](BackEnd/README.md).

```bash
dotnet test BackEnd/MyCloud.slnx
```

Frontend (bản cũ, chưa nối API thật):

```bash
cd FrontEnd/my-drive && npm install && npm start
```
