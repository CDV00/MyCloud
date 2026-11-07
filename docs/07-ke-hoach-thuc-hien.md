# 07 — Kế hoạch thực hiện

## 1. Nguyên tắc lập kế hoạch

1. **Sửa nền trước, mở rộng sau**: xác thực, quyền sở hữu, lưu trữ file là lõi — làm đúng trước khi thêm tính năng.
2. **Mỗi phase kết thúc bằng một bản chạy được end-to-end** (backend + frontend + test), không làm "hết backend rồi mới làm frontend".
3. **Tái sử dụng code cũ có chọn lọc** (bảng mapping ở mục 4) — không viết lại những gì đang tốt.
4. Làm trên nhánh, merge qua PR có CI xanh.

Ước lượng tính cho **1 developer full-time**: tổng ≈ 20 tuần (≈ 5 tháng), MVP sau ≈ 8–9 tuần. Với 2–3 người, từ Phase 2 có thể chạy song song (Drive / Storage / Frontend) và rút còn ≈ 3 tháng.

## 2. Lộ trình tổng quan

```mermaid
gantt
    dateFormat YYYY-MM-DD
    axisFormat %d/%m
    title Lộ trình MyCloud (1 developer)
    section Nền tảng
    Phase 0 - Tái cấu trúc & hạ tầng      :p0, 2026-11-09, 14d
    Phase 1 - Identity, Gateway, Web nền  :p1, after p0, 14d
    section MVP
    Phase 2 - Drive + Storage + Upload    :crit, p2, after p1, 32d
    section Tính năng
    Phase 3 - Thùng rác, Version, Search  :p3, after p2, 21d
    Phase 4 - Chia sẻ & Thông báo         :p4, after p3, 25d
    Phase 5 - Media & Trải nghiệm         :p5, after p4, 18d
    section Production
    Phase 6 - Hardening & Triển khai      :p6, after p5, 21d
```

| Mốc | Sau phase | Ý nghĩa |
|---|---|---|
| **M1 — MVP** | 2 | Đăng ký, đăng nhập, cây thư mục, upload file lớn (resume), tải về; dữ liệu cách ly theo user |
| **M2 — Beta** | 4 | Thùng rác, version, tìm kiếm, gần đây, chia sẻ (user + link), thông báo |
| **M3 — Production** | 6 | Thumbnail/preview, quan sát, bảo mật đã rà soát, triển khai K8s/VPS, backup |

---

## Phase 0 — Tái cấu trúc & hạ tầng (≈ 2 tuần)

**Mục tiêu**: repo sạch, chạy toàn bộ hạ tầng bằng 1 lệnh, CI hoạt động.

- [x] Cài .NET 10 SDK; tạo `global.json`; nâng mọi project lên `net10.0`.
- [x] `Directory.Build.props` (Nullable, ImplicitUsings, `TreatWarningsAsErrors`), `Directory.Packages.props` (Central Package Management); ghim MassTransit 8.5.x, MediatR 12.5.x; bỏ AutoMapper, Quartz, Newtonsoft, Swashbuckle, Serilog (log đi qua OpenTelemetry).
- [x] Tạo `MyCloud.slnx` theo cấu trúc ở [02 §8](02-kien-truc-tong-the.md#8-cấu-trúc-thư-mục-mục-tiêu); đổi tên project/namespace, sửa lỗi chính tả (`BuidingBlock` → `BuildingBlocks`...). Command → **Drive** (4 project), Query → **Search** (1 project), Auth → **Identity.Api** (1 project, tách ở Phase 1).
- [x] Tách BuildingBlocks: `Domain`, `Application` (pipeline Performance → Validation → UnitOfWork), `Messaging`, `Grpc` (interceptor map lỗi, deadline), `Web` (ProblemDetails, versioning, OpenAPI + Scalar, Carter).
- [x] `MyCloud.Contracts.Events` (không phụ thuộc gì) và `Contracts/Protos` (thư mục `.proto` + `buf.yaml`, không phải csproj — service link file proto trực tiếp).
- [x] Aspire `AppHost` + `ServiceDefaults` (OTel, health `/health/live|ready`, resilience, service discovery). Postgres, Mongo, RabbitMQ, Redis, MinIO, Mailpit. *(Mongo replica set để Phase 3 khi cần transaction.)*
- [x] `deploy/docker-compose.yml` cho hạ tầng *(image service để Phase 6)*.
- [x] Chuyển EF Core sang **PostgreSQL** (Npgsql, snake_case), mỗi service 1 database, xoá migration SQL Server cũ, tạo migration mới.
- [x] Gỡ toàn bộ secret khỏi `appsettings*.json`; khoá JWT tạm thời do Aspire sinh + lưu user-secrets; gitleaks chạy trong CI. **Khoá cũ đã nằm trong lịch sử git — coi như lộ.**
- [x] Thay outbox tự viết (Quartz) bằng **MassTransit EF Core Outbox** (Drive, Identity); bỏ `TypeNameHandling`, dùng serializer mặc định.
- [x] Test projects: `Drive.Domain.Tests`, `Drive.IntegrationTests` (Testcontainers + MassTransit test harness), `Architecture.Tests` (NetArchTest + kiểm tra không tham chiếu chéo service).
- [x] GitHub Actions `backend-ci.yml`: build + test + `dotnet format --verify-no-changes` + vulnerable packages + `buf lint/breaking` + gitleaks.
- [x] `README.md` gốc và `BackEnd/README.md`: cách chạy, cấu trúc.

Sửa thêm trong Phase 0 (vì code được chuyển sang cấu trúc mới): bug `Register` (S8), lỗi đăng nhập lộ user (S9), `UpdateFile` xoá liên kết nội dung (S6), 500 khi id không tồn tại (S7), xoá thư mục bỏ mặc con (S10 — tạm chặn xoá thư mục không rỗng), audit `CreatedBy = Guid.NewGuid()` (S2, giờ lấy từ `ICurrentUser`), poll outbox 10.000 lần/s (R1), projection không idempotent/sai thứ tự (R3, R5), đường dẫn lưu file hard-code (S5 — tạm lưu ổ đĩa theo cấu hình đến Phase 2), CORS `*`.

**Hoàn thành khi**: `dotnet run --project src/Aspire/MyCloud.AppHost` khởi động mọi container + service khung, dashboard xanh; CI xanh trên PR; `git grep SecretKey` không còn giá trị thật.

---

## Phase 1 — Identity, Gateway, Web nền (≈ 2 tuần)

**Backend**
- [ ] Identity: register / login / refresh (rotation + phát hiện reuse) / logout / `users/me` / đổi mật khẩu. Sửa bug `Register` (map null), bỏ role hard-code, thông báo lỗi không lộ user.
- [ ] Rút gọn `AppUser` (bỏ trường HR), thêm `DisplayName`, `AvatarUrl`.
- [ ] JWT **RS256** + endpoint JWKS; key rotation cơ bản.
- [ ] Publish `UserRegistered` qua outbox.
- [ ] gRPC `UserService` (`GetUser`, `BatchGetUsers`, `FindUserByEmail`).
- [ ] `BuildingBlocks.Web`: cấu hình JWT bearer từ JWKS, `ICurrentUser`, ProblemDetails, Carter + versioning.
- [ ] Gateway: bật authentication, route `/api/{service}/**`, CORS theo cấu hình, rate limit (login chặt), `X-Correlation-Id`, xoá header nội bộ từ client.

**Frontend**
- [ ] Tạo `FrontEnd/web`: Vite + React + TypeScript, ESLint, Prettier, Vitest.
- [ ] Port layout cũ (`Home`, `home.scss`, menu điều hướng) sang TS + MUI.
- [ ] Trang đăng nhập/đăng ký thật; API client (fetch/axios) có interceptor tự refresh token; `ProtectedRoute` dựa trên trạng thái auth thật.
- [ ] TanStack Query cho data fetching.

**Test**: integration Identity (đăng ký trùng email, sai mật khẩu, lockout, refresh reuse).

**Hoàn thành khi**: đăng ký + đăng nhập từ web; mọi API không có token → 401; refresh hoạt động sau 15 phút.

---

## Phase 2 — Drive + Storage + Upload/Download → **MVP** (≈ 4–5 tuần)

**Drive service** (refactor từ `MyDrive.Command.*`)
- [ ] Aggregate `DriveItem` + `FileVersion` (thay `Folder`/`File`), value object `ItemName`, `MaterializedPath`.
- [ ] Schema Postgres + index (xem [03 §3](03-dac-ta-service.md#3-drive-service-trung-tâm)).
- [ ] Consumer `UserRegistered` → tạo root; `GET /items/root` tạo-nếu-chưa-có.
- [ ] Tạo thư mục, liệt kê con (cursor paging, sort, folder trước), breadcrumb, đổi tên (`If-Match`), di chuyển (update `path` cả cây, chặn di chuyển vào con cháu).
- [ ] Phân quyền giai đoạn này: **chỉ chủ sở hữu** (`IItemAuthorizer` có sẵn interface để Phase 4 cắm Sharing vào).
- [ ] Upload: `POST /uploads`, `:complete`, `DELETE`; bảng `pending_uploads`; xử lý trùng tên.
- [ ] Download: `POST /items/{id}:downloadUrl`.
- [ ] Publish `ItemCreated`, `ItemRenamed`, `ItemMoved`, `FileVersionAdded`.

**Storage service** (thay `MyDriver.BuildBlock.FTP`)
- [ ] MinIO client (AWSSDK.S3 hoặc Minio SDK), tạo bucket khi khởi động, bucket policy private, lifecycle abort multipart.
- [ ] Bảng `blobs`, `upload_sessions`, `quotas`; reserve/commit/release quota atomic.
- [ ] gRPC `StorageService` (CreateUploadSession, SignUploadParts, CompleteUpload, AbortUpload, GetDownloadUrl, GetQuota).
- [ ] REST `GET /uploads/{id}/parts`, `GET /quota`.
- [ ] Consumer `UserRegistered` (quota), `FileVersionAdded` (claim).
- [ ] Job: hết hạn session, GC blob không được claim.

**gRPC nền tảng**
- [ ] `BuildingBlocks.Grpc`: interceptor deadline, chuyển tiếp token, map lỗi ↔ `Result`, logging; endpoint Kestrel HTTP/2 riêng.

**Frontend**
- [ ] Trang "Drive của tôi": dạng lưới/danh sách, breadcrumb, sắp xếp, chọn nhiều.
- [ ] Tạo thư mục, đổi tên, di chuyển (dialog chọn thư mục), menu chuột phải.
- [ ] Upload bằng **Uppy + AwsS3 (multipart)** với endpoint tuỳ biến trỏ tới Drive/Storage; kéo thả; thanh tiến trình; resume.
- [ ] Tải xuống; thanh dung lượng (quota).

**Test**
- [ ] Domain tests cho `DriveItem` (đổi tên, di chuyển, invariant).
- [ ] Integration: upload end-to-end với MinIO Testcontainer; vượt quota; **IDOR** (user B truy cập item của A → 404).

**Hoàn thành khi (MVP)**: upload file 1 GB, ngắt mạng giữa chừng vẫn resume được; tạo/di chuyển/đổi tên thư mục; tải về đúng tên file; hai user hoàn toàn cách ly dữ liệu; trace một lần upload xem được trọn vẹn trên dashboard.

---

## Phase 3 — Thùng rác, Version, Gắn sao, Search (≈ 3 tuần)

**Drive**
- [ ] Trash / restore (cả cây con, khôi phục về root nếu cha đã mất) / xoá vĩnh viễn / dọn thùng rác; job purge 30 ngày; event `ItemTrashed`, `ItemRestored`, `ItemsPurged` (theo lô).
- [ ] Version: upload đè tạo version mới, liệt kê, khôi phục; giới hạn số version giữ lại (cấu hình).
- [ ] Sao chép file (`CopyBlob`) và thư mục (job nền).
- [ ] Gắn sao, `GET /starred`. Event `ItemAccessed` (gộp 5 phút).

**Storage**: consumer `ItemsPurged` → xoá object + trả quota (idempotent).

**Search service** (tái định hướng từ `MyDrive.Query.*`)
- [ ] Đổi tên, giữ lại `MongoRepository` (sửa: `MongoClient` singleton, bỏ `Task.Run`, `await` đầy đủ).
- [ ] Read model `search_items`, `recent`, `activity`; consumer cho các event của Drive, áp dụng theo `Revision`; idempotency bằng outbox/inbox Mongo.
- [ ] `GET /search` (tên, loại, ngày, chủ sở hữu), `/recent`, `/items/{id}/activity`.

**Frontend**: trang Thùng rác, Có gắn dấu sao, Gần đây; ô tìm kiếm + bộ lọc; panel chi tiết (thông tin, hoạt động, version).

**Hoàn thành khi**: xoá thư mục 1.000 file → khôi phục đầy đủ; xoá vĩnh viễn → object trong MinIO biến mất và quota giảm; tìm thấy file vừa upload trong < 2 s.

---

## Phase 4 — Chia sẻ & Thông báo (≈ 3–4 tuần)

**Sharing service**
- [ ] OpenFGA: container, store, authorization model (xem [03 §5](03-dac-ta-service.md#5-sharing-service)), migration model trong CI.
- [ ] Consumer `ItemCreated` / `ItemMoved` / `ItemsPurged` → tuple; `fga_tuple_outbox` + worker đảm bảo DB và OpenFGA nhất quán.
- [ ] REST quyền: liệt kê (kèm quyền kế thừa), thêm theo email (gRPC Identity), đổi vai trò, thu hồi.
- [ ] Link chia sẻ: tạo / tắt / truy cập anonymous; mật khẩu & hạn dùng.
- [ ] gRPC `AuthorizationService` (Check, BatchCheck với contextual tuples tổ tiên, GetEffectiveRole).

**Drive**: `IItemAuthorizer` = fast-path chủ sở hữu → Sharing; trả `capabilities` cho từng item; hỗ trợ `X-Share-Token`.

**Search**: `access_grants`, `GET /shared-with-me`, lọc kết quả tìm kiếm theo quyền + `BatchCheck` xác nhận.

**Notification service**
- [ ] Lưu liên hệ từ `UserRegistered`; email qua SMTP (Mailpit dev) cho `ItemShared`, `QuotaThresholdReached`.
- [ ] Thông báo in-app + SignalR hub (Gateway proxy WebSocket, Redis backplane).

**Frontend**: hộp thoại chia sẻ (thêm người, vai trò, link), trang "Được chia sẻ với tôi", trang mở link công khai, chuông thông báo realtime.

**Test**: **ma trận quyền** (owner/editor/commenter/viewer/không quyền × xem/sửa/xoá/chia sẻ × trực tiếp/kế thừa/qua link) chạy tự động.

**Hoàn thành khi**: A chia sẻ thư mục cho B quyền xem → B nhận thông báo realtime, thấy thư mục và mọi file con, không sửa được; A thu hồi → B mất quyền ngay lập tức.

---

## Phase 5 — Media & Trải nghiệm (≈ 2–3 tuần)

- [ ] Media Worker: thumbnail ảnh, khung hình video (ffmpeg), trang đầu PDF; `ThumbnailGenerated` → Drive.
- [ ] Xem trước trên web: ảnh (lightbox), PDF (PDF.js), video/audio (presigned `inline`), text/code.
- [ ] Tải nhiều item / cả thư mục dạng zip (job nền stream vào MinIO, thông báo khi xong).
- [ ] Upload cả thư mục (giữ cấu trúc), xử lý xung đột tên hàng loạt.
- [ ] Trang "Bộ nhớ": dung lượng theo loại file, file lớn nhất.
- [ ] (Tuỳ chọn) ClamAV + `MalwareDetected`.

---

## Phase 6 — Hardening & Triển khai (≈ 3 tuần)

- [ ] Stack quan sát production (Prometheus, Grafana, Tempo, Loki), dashboard + cảnh báo ở [06 §4](06-ha-tang-bao-mat-van-hanh.md#4-quan-sát-observability).
- [ ] Rà soát bảo mật theo OWASP ASVS L2; OWASP ZAP baseline; CSP; Trivy; xoay khoá JWT.
- [ ] Load test k6 (liệt kê thư mục 10k item, 200 upload đồng thời), tối ưu index/truy vấn.
- [ ] Helm chart, môi trường staging, pipeline deploy, migration job.
- [ ] Backup/restore đã diễn tập (Postgres, Mongo, MinIO); runbook sự cố (queue `_error`, outbox tồn đọng, MinIO đầy).
- [ ] Identity bổ sung: xác minh email, quên mật khẩu, (tuỳ chọn) đăng nhập Google, 2FA.

---

## 3. Việc làm ngay tuần đầu

1. Cài .NET 10 SDK, tạo nhánh `feature/restructure`.
2. Tạo `Directory.Build.props`, `Directory.Packages.props`, `global.json`.
3. Tạo Aspire AppHost với Postgres/RabbitMQ/Redis/MinIO/Mongo — chạy được hạ tầng trước khi đụng code service.
4. Tách `BuildingBlocks.Domain` + `Contracts.Events` từ `MyDrive.BuidingBlock.Contract`.
5. Chuyển Identity sang Postgres + sửa bug `Register` — service nhỏ nhất, làm mẫu cho các service sau.

> Không nên vá riêng code Command/Query cũ (upload, delete…) vì sẽ được thay thế ở Phase 2–3. Ngoại lệ: nếu cần chạy demo trước, sửa ngay `TimeSpan.FromMicroseconds(100)` → `FromSeconds(1)` và bug `Register`.

## 4. Mapping code cũ → kiến trúc mới

| Code cũ | Đi đâu | Ghi chú |
|---|---|---|
| `MyDrive.BuidingBlock.Contract/Abstractions/Shared/*` (Result, Error, PagedResult) | `BuildingBlocks.Domain` | Giữ nguyên ý tưởng |
| `.../Abstractions/Messages/*` (ICommand, IQuery, handler) | `BuildingBlocks.Application` | Bỏ `IDomainEvent : ICommand` |
| `.../Services/V1/*/DomainEvent.cs` | `Contracts.Events` | Viết lại thành integration event mới |
| `.../Services/V1/*/Command/Query/Response.cs` | Trong từng service (`Drive.Application`) | Không chia sẻ DTO nội bộ qua Contracts; bỏ `IFormFile` |
| `.../JsonConverters/*` | Bỏ | Dùng System.Text.Json |
| `MyDriver.BuildBlock.FTP` | Bỏ → **Storage service** | |
| `MyDrive.Command.Domain` (AggregateRoot, Entity, ValueObject, exceptions) | `BuildingBlocks.Domain` + `Drive.Domain` | `Folder`/`File` → `DriveItem` |
| `MyDrive.Command.Application/Behaviors/*` | `BuildingBlocks.Application` | Giữ Validation, Performance, Tracing; Transaction → dựa trên outbox |
| `MyDrive.Command.Persistence` | `Drive.Infrastructure` | Interceptor audit: lấy `ICurrentUser` thay `Guid.NewGuid()`; bỏ outbox interceptor tự viết |
| `MyDrive.Command.Infrastructure` (MassTransit, Quartz outbox, observers) | `BuildingBlocks.Messaging` | Bỏ Quartz outbox; giữ observer logging (hoặc thay bằng OTel) |
| `MyDrive.Command.Presentation` + `.Api` | `Drive.Api` | Carter modules, middleware |
| `MyDrive.Query.*` | **Search service** | Repo Mongo giữ + sửa; consumer viết lại theo Revision |
| `Auth.Api` | **Identity service** (4 project) | Giữ Identity + EF config; viết lại token & refresh |
| `ApiGateway` | `MyCloud.Gateway` | Bật JWT, thêm route, rate limit |
| `FrontEnd/my-drive` (CRA) | `FrontEnd/web` (Vite + TS) | Port layout, SCSS, cấu trúc router |

## 5. Rủi ro & cách giảm thiểu

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Phạm vi quá lớn cho 1 người | Không bao giờ xong | Mốc MVP rõ (Phase 2); Phase 5–6 có thể cắt bớt |
| Độ phức tạp microservice (debug phân tán) | Chậm | Aspire dashboard + OTel từ Phase 0; integration test với Testcontainers |
| Eventual consistency gây lỗi UX | Người dùng thấy dữ liệu cũ | Đọc nhất quán từ Drive cho các màn hình chính; Search chỉ cho view chấp nhận trễ; frontend optimistic update |
| OpenFGA là công nghệ mới với nhóm | Chậm Phase 4 | Spike 2 ngày đầu Phase 4; nếu không phù hợp, fallback: bảng ACL + truy vấn theo `path` trong Postgres (ADR-007) |
| Thay đổi license thư viện (.NET OSS) | Chi phí / phải thay thư viện | Ghim major, bọc sau abstraction trong BuildingBlocks (ADR-008) |
| Mất dữ liệu file | Nghiêm trọng | Không bao giờ xoá blob đồng bộ; xoá qua event + GC có độ trễ; MinIO versioning; backup |
| Thay đổi hợp đồng phá service khác | Lỗi runtime | `buf breaking` trong CI; event versioning |
