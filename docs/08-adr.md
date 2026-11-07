# 08 — Architecture Decision Records (ADR)

Mỗi quyết định ghi: bối cảnh → quyết định → hệ quả. Trạng thái: **Accepted** trừ khi ghi khác. Thay đổi quyết định ⇒ thêm ADR mới "Supersedes ADR-xxx", không sửa ADR cũ.

---

<a id="adr-001"></a>
## ADR-001 — Kiến trúc microservice, giao tiếp gRPC (đồng bộ) + RabbitMQ (bất đồng bộ)

**Bối cảnh**: yêu cầu của dự án là microservice; các service cần vừa truy vấn tức thời (kiểm tra quyền, xin URL upload) vừa phản ứng với sự kiện (dọn dữ liệu, index, thông báo).

**Quyết định**:
- gRPC cho lời gọi nội bộ cần kết quả ngay; đồ thị phụ thuộc không vòng, tối đa 2 hop, deadline mặc định 2 s.
- Integration event qua RabbitMQ (MassTransit) cho mọi thay đổi trạng thái mà service khác quan tâm, publish qua transactional outbox.
- Client chỉ nói chuyện REST/JSON (+ SignalR) với Gateway; không expose gRPC ra ngoài.

**Hệ quả**: + Hiệu năng tốt, hợp đồng chặt (protobuf), service độc lập khi broker trễ. − Phải vận hành RabbitMQ, xử lý eventual consistency và idempotency.

---

<a id="adr-002"></a>
## ADR-002 — Dữ liệu file đi trực tiếp client ↔ object storage bằng presigned URL

**Bối cảnh**: code cũ đẩy toàn bộ file qua Command API (`IFormFile`) và đọc toàn bộ vào RAM khi tải.

**Quyết định**: dùng MinIO/S3; Storage service chỉ phát presigned URL (multipart cho upload). Byte của file không đi qua Gateway hay service .NET.

**Hệ quả**: + Chịu được file nhiều GB, resume, tiết kiệm CPU/RAM, scale theo object storage. − Cần CORS trên bucket, xử lý blob mồ côi (ADR-006), không thể chặn nội dung "trước khi" ghi (quét virus làm sau).

---

<a id="adr-003"></a>
## ADR-003 — Gộp đường đọc chính vào Drive service; Query service cũ thành Search service

**Bối cảnh**: code cũ tách Command (SQL) và Query (Mongo) thành 2 service cho cùng dữ liệu folder/file. Người dùng Drive kỳ vọng thấy ngay thứ họ vừa tạo; độ trễ projection gây lỗi UX.

**Quyết định**: Drive phục vụ cả ghi và đọc nhất quán (liệt kê thư mục, chi tiết item) từ PostgreSQL, áp dụng CQRS ở tầng code (command handler / query handler riêng) chứ không tách service. Query service trở thành **Search service** cho các view tổng hợp, chấp nhận trễ.

**Hệ quả**: + Read-after-write cho màn hình chính, bớt 1 điểm lỗi trên đường đi quan trọng. + MongoDB/projection vẫn được dùng đúng chỗ. − Drive chịu tải đọc lớn hơn (giảm bằng index, read replica khi cần).

---

<a id="adr-004"></a>
## ADR-004 — PostgreSQL thay SQL Server LocalDB, database-per-service

**Bối cảnh**: LocalDB chỉ chạy trên Windows, khó container hoá; Auth và Command đang dùng chung database.

**Quyết định**: PostgreSQL (1 server dev, mỗi service 1 database riêng, user riêng). Migration cũ chỉ có 3 bản khởi tạo nên tạo lại từ đầu.

**Hệ quả**: + Chạy mọi nơi, miễn phí, có `text_pattern_ops`/`ltree`/recursive CTE cho cây thư mục. − Đổi provider EF (Npgsql), cú pháp SQL thô (nếu có) phải viết lại.

---

<a id="adr-005"></a>
## ADR-005 — Một aggregate `DriveItem` cho cả file và thư mục, cây lưu bằng adjacency list + materialized path

**Bối cảnh**: liệt kê/sắp xếp/phân trang hỗn hợp, di chuyển, thùng rác, chia sẻ đều áp dụng chung cho file và thư mục.

**Quyết định**: bảng `items` với `kind`; `parent_id` (adjacency) + `path` (chuỗi ID tổ tiên). Nội dung file nằm ở `file_versions`.

**Hệ quả**: + 1 query liệt kê, 1 câu UPDATE di chuyển cả cây, truy vấn cây con bằng `LIKE 'prefix%'`. − `path` phải cập nhật khi di chuyển (tốn với cây rất lớn — chấp nhận, làm trong 1 transaction).

---

<a id="adr-006"></a>
## ADR-006 — Không dùng distributed transaction giữa Drive và Storage; dùng trạng thái blob + claim + GC

**Bối cảnh**: hoàn tất upload cần cả Storage (object + quota) và Drive (metadata) thành công.

**Quyết định**: Storage hoàn tất trước (blob `Uploaded`), Drive ghi metadata + outbox `FileVersionAdded`; Storage consume event để `Claim`. Blob không được claim sau 24 h bị GC và trả quota. Xoá blob luôn bất đồng bộ qua `ItemsPurged`.

**Hệ quả**: + Không 2PC, mọi bước idempotent, không bao giờ mất file đã được ghi nhận. − Quota có thể "tạm giữ" tối đa 24 h với upload lỗi.

---

<a id="adr-007"></a>
## ADR-007 — OpenFGA cho phân quyền kế thừa

**Bối cảnh**: quyền chia sẻ kế thừa xuống cây thư mục, cần trả lời cả "A có quyền X trên item Y?" lẫn "A được chia sẻ những gì?".

**Quyết định**: Sharing service dùng OpenFGA (mô hình Zanzibar). Drive gửi danh sách tổ tiên làm contextual tuples để không phụ thuộc độ trễ event. Fast-path chủ sở hữu kiểm tra ngay trong Drive.

**Phương án thay thế đã cân nhắc**: bảng ACL trong Postgres của Drive + truy vấn theo `path` (đơn giản hơn, ít hạ tầng hơn, nhưng logic kế thừa/thu hồi/liệt kê tự viết và gắn chặt vào Drive). Giữ làm **fallback** nếu spike đầu Phase 4 cho thấy OpenFGA không phù hợp.

**Hệ quả**: + Mô hình quyền khai báo, test được, mở rộng (nhóm, domain) dễ. − Thêm 1 thành phần hạ tầng; phải đồng bộ tuple với DB (outbox).

---

<a id="adr-008"></a>
## ADR-008 — Quản lý rủi ro license thư viện .NET (MassTransit, MediatR, AutoMapper)

**Bối cảnh**: trong 2025 các tác giả công bố chuyển sang license thương mại cho các bản major mới: MassTransit từ v9, MediatR và AutoMapper ở các bản mới (có điều khoản community cho tổ chức nhỏ). Các bản cũ (MassTransit 8.x, MediatR 12.x, AutoMapper ≤ 14) vẫn là Apache-2.0/MIT. *Cần kiểm tra lại điều khoản hiện hành trước khi phát hành thương mại.*

**Quyết định**:
- Ghim **MassTransit 8.x** và **MediatR 12.x** qua Central Package Management; mọi cấu hình bus nằm trong `BuildingBlocks.Messaging` để có thể thay thư viện mà không sửa service.
- **Bỏ AutoMapper** (map thủ công/extension method — ít "magic", dễ debug).
- Phương án dự phòng khi cần thay: **Wolverine** (MIT, có outbox + RabbitMQ + mediator) hoặc mua license MassTransit.

**Hệ quả**: + Không phát sinh chi phí ngay, có đường lui. − Bản 8.x sẽ dần ngừng nhận bản vá — cần đánh giá lại trước Phase 6.

---

<a id="adr-009"></a>
## ADR-009 — Nâng cấp .NET 10 (LTS) và dùng .NET Aspire cho môi trường dev

**Bối cảnh**: .NET 8 hết hỗ trợ ngày 10/11/2026. Dev hiện phải tự chạy RabbitMQ (compose), Redis (`docker run`), LocalDB, Mongo — rời rạc.

**Quyết định**: chuyển toàn bộ sang .NET 10 LTS; dùng Aspire AppHost để orchestration local, ServiceDefaults cho OTel/health/resilience. Production không phụ thuộc Aspire (Helm/compose).

**Hệ quả**: + Được vá bảo mật, 1 lệnh chạy toàn hệ thống, trace phân tán có sẵn. − Phải cài SDK mới, học Aspire.

---

<a id="adr-010"></a>
## ADR-010 — Identity tự xây (ASP.NET Core Identity) với JWT RS256, chưa dùng Keycloak

**Bối cảnh**: đã có `Auth.Api` dựa trên ASP.NET Core Identity. Keycloak/Duende cung cấp OIDC đầy đủ nhưng là thành phần lớn (Keycloak) hoặc có license (Duende).

**Quyết định**: giữ Identity service tự xây, nâng cấp: RS256 + JWKS, refresh token rotation, rate limit. Thiết kế claim/endpoint theo chuẩn OIDC (`sub`, `/.well-known/jwks.json`) để có thể thay bằng Keycloak sau mà Gateway/service không đổi.

**Hệ quả**: + Ít hạ tầng, tận dụng code có sẵn, kiểm soát hoàn toàn. − Tự chịu trách nhiệm bảo mật luồng đăng nhập; social login/2FA phải tự làm.

---

<a id="adr-011"></a>
## ADR-011 — Object storage: code theo API S3, MinIO chỉ là một lựa chọn triển khai

**Trạng thái**: Accepted (Phase 0), xem lại đầu Phase 2.

**Bối cảnh**: khi chạy Phase 0 (06/11/2026), image `minio/minio` không còn kéo được từ Docker Hub lẫn Quay — MinIO đã ngừng phát hành image/binary cho bản community. Bản build từ source của Chainguard (`cgr.dev/chainguard/minio`) vẫn dùng được nhưng bản miễn phí chỉ có tag `latest`.

**Quyết định**:
- Storage service chỉ nói chuyện qua **API S3** (AWS SDK for .NET hoặc thư viện S3 tương thích), không dùng SDK riêng của MinIO → backend có thể thay mà không sửa code.
- Dev/Phase 0: dùng `cgr.dev/chainguard/minio` **ghim theo digest** trong AppHost và docker-compose.
- Đầu Phase 2 đánh giá lựa chọn lâu dài cho self-host: MinIO (build từ source / bản Chainguard), **SeaweedFS**, **Garage**, RustFS; production trên cloud có thể dùng thẳng AWS S3 / Cloudflare R2. Tiêu chí: hỗ trợ multipart + presigned URL + CORS, license, bản phát hành đều đặn.

**Hệ quả**: + Không khoá vào một nhà cung cấp; Phase 0 vẫn chạy được. − Digest phải cập nhật thủ công khi có bản vá bảo mật cho tới khi chốt lựa chọn.
