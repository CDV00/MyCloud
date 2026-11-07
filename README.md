# MyCloud

Ứng dụng lưu trữ đám mây kiểu Google Drive — backend microservice .NET 10 (gRPC + RabbitMQ), frontend React + TypeScript.

- **Kiến trúc, đặc tả, kế hoạch**: [docs/](docs/README.md)
- **Trạng thái**: Phase 4 xong — chia sẻ theo email (người xem / nhận xét / chỉnh sửa, kế thừa theo thư mục), link công khai có mật khẩu, "Được chia sẻ với tôi", thông báo realtime + email. Trước đó: Phase 3 xong — MVP (cây thư mục, upload multipart trực tiếp lên object storage, tải về, dung lượng) + thùng rác, phiên bản, sao chép, gắn sao, tìm kiếm không dấu, "Gần đây", nhật ký hoạt động. Tiếp theo Phase 4 (chia sẻ, thông báo). Xem [kế hoạch](docs/07-ke-hoach-thuc-hien.md).

## Cấu trúc

```
BackEnd/       .NET 10 — MyCloud.slnx: Aspire AppHost, Gateway (YARP), Identity, Drive, Search,
               Storage/Sharing/Notification/Media (khung), BuildingBlocks, Contracts, tests
FrontEnd/web/  Vite + React 19 + TypeScript + MUI + TanStack Query
deploy/        docker-compose cho hạ tầng (khi không dùng Aspire)
docs/          tài liệu kiến trúc, ADR, kế hoạch
```

## Chạy nhanh

Yêu cầu: .NET SDK 10, Docker Desktop, Node.js ≥ 20.18 (khuyến nghị 24 LTS).

```bash
dotnet run --project BackEnd/src/Aspire/MyCloud.AppHost
```

Lệnh trên khởi động toàn bộ hạ tầng (PostgreSQL, MongoDB, RabbitMQ, Redis, MinIO, Mailpit), các service và web app. Mở **http://localhost:5173** để đăng ký tài khoản. Chi tiết: [BackEnd/README.md](BackEnd/README.md).

```bash
dotnet test BackEnd/MyCloud.slnx
```

Frontend chạy riêng (cần Gateway đang chạy ở http://localhost:5000):

```bash
cd FrontEnd/web && npm install && npm run dev
```

```bash
cd FrontEnd/web && npm test
```
