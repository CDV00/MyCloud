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

---

<a id="adr-012"></a>
## ADR-012 — Phát hành và lưu token cho web app

**Trạng thái**: Accepted (Phase 1).

**Bối cảnh**: SPA cần giữ phiên đăng nhập qua các lần tải trang mà không để XSS lấy được token dùng lâu dài. Bản cũ lưu chuỗi giả vào `localStorage`. Phase 0 ký JWT bằng HMAC với bí mật dùng chung: service nào validate được token thì cũng **phát hành** được token.

**Quyết định**:
- **RS256**: chỉ Identity giữ private key; Gateway/service tải public key từ `/.well-known/jwks.json` (qua discovery document) và tự validate.
- **Khoá ký** lưu DB (`signing_keys`), private key mã hoá bằng ASP.NET Core Data Protection (key ring cũng trong DB; production bảo vệ key ring bằng certificate / Key Vault). Xoay vòng tự động: khoá mới được công bố trong JWKS **2 giờ trước** khi dùng để ký (service tải lại JWKS mỗi giờ), khoá cũ còn trong JWKS thêm 1 ngày. Nhiều instance Identity phối hợp bằng PostgreSQL advisory lock.
- **Access token** (15 phút) trả trong body; SPA chỉ giữ trong bộ nhớ.
- **Refresh token** (30 ngày, xoay vòng mỗi lần dùng, phát hiện dùng lại) nằm trong cookie `HttpOnly; SameSite=Strict`, `Path` chỉ là đường dẫn auth. Client không phải trình duyệt gửi `refreshTokenInBody: true` để nhận trong body.
- **SPA và API cùng origin**: dev dùng Vite proxy `/api` → Gateway; production đặt SPA và Gateway sau cùng một domain. Không cần CORS cho web app, cookie `SameSite=Strict` hoạt động, và `/auth/refresh` bắt buộc body JSON nên form của site khác không gọi được.

**Phương án đã loại**: lưu token trong `localStorage` (XSS đọc được token dài hạn); BFF giữ token phía server (an toàn hơn nữa nhưng thêm một thành phần có trạng thái — xem lại nếu làm đăng nhập Google/OIDC).

**Hệ quả**: + Token lộ qua XSS chỉ sống tối đa 15 phút; refresh token bị đánh cắp rồi dùng lại sẽ bị phát hiện. + Thay Identity bằng Keycloak sau này không đổi Gateway/service (chuẩn JWKS). − Đổi mật khẩu thu hồi refresh token ngay nhưng access token đã phát hành còn hiệu lực tới khi hết hạn (≤ 15 phút). − Mất key ring của Data Protection ⇒ khoá ký cũ không đọc được, hệ thống tự tạo khoá mới và mọi người phải đăng nhập lại.

---

<a id="adr-013"></a>
## ADR-013 — Presigned URL ký theo origin của trình duyệt, object storage đi cùng origin

**Trạng thái**: Accepted (Phase 2). Bổ sung ADR-002, ADR-011.

**Bối cảnh**: trình duyệt PUT/GET byte của file thẳng tới object storage bằng presigned URL (ADR-002). Chữ ký S3 V4 gồm cả **host**, nên URL phải được ký đúng địa chỉ mà trình duyệt gọi. Cùng một hệ thống được mở qua `localhost:5173` (Vite), `mycloud.localhost` (nginx) và `10.x.x.x:8081` (máy khác trong LAN); MinIO trong dev chỉ nghe trên `127.0.0.1`.

**Quyết định**:
- Object storage được **proxy cùng origin** với web app theo tiền tố đường dẫn là tên bucket (`/mycloud-blobs/...`, path-style): Vite proxy trong dev, nginx khi chạy qua tên miền / IP. Proxy giữ nguyên header `Host`.
- Storage ký URL cho **gốc URL công khai của request** (`X-Forwarded-Proto/Host` do Gateway đặt, Drive truyền qua gRPC). Ký cho một host bất kỳ không mở thêm quyền: URL vẫn chỉ dùng được cho đúng object, đúng thao tác, trong thời hạn ngắn.
- Production dùng S3/R2 thật (hoặc CDN): đặt `Storage:PublicBaseUrl` ⇒ mọi URL ký theo địa chỉ đó, bật CORS trên bucket cho origin của web app.
- Lựa chọn object storage tự host lâu dài (ADR-011) **dời tới Phase 6** khi chốt hạ tầng triển khai: code chỉ dùng API S3 nên đổi MinIO ↔ SeaweedFS / Garage / S3 / R2 không phải sửa service.

**Hệ quả**: + Không cần CORS ở dev, cookie và mọi request cùng origin; hoạt động qua localhost, tên miền và IP. + Byte của file vẫn không đi qua Gateway hay service .NET. − Khi tự host, băng thông upload/download đi qua reverse proxy (nginx), cần cấu hình không buffer và timeout phù hợp.


---

<a id="adr-014"></a>
## ADR-014 — Quyền chia sẻ lưu trong PostgreSQL (thay OpenFGA), Drive là mặt tiền REST, định danh service

**Trạng thái**: Accepted (Phase 4). Thay phần "OpenFGA" của ADR-007 (giữ fast-path chủ sở hữu và việc Drive gửi kèm tổ tiên).

**Bối cảnh**: ADR-007 chọn OpenFGA và để ngỏ phương án dự phòng (ACL trong Postgres) nếu spike đầu Phase 4 cho thấy không
phù hợp. Khi làm, mọi lần kiểm tra quyền đều do Drive gọi và Drive **luôn biết chủ sở hữu + danh sách tổ tiên** của item
(materialized path). Với dữ liệu đó, "vai trò hiệu lực" chỉ là: chủ sở hữu? ⇒ owner; ngược lại max(vai trò của quyền
trực tiếp trên item hoặc bất kỳ tổ tiên nào, vai trò của link còn hiệu lực). Đó là **một truy vấn** trên bảng `permissions`.
OpenFGA khi đó chỉ lưu đúng các tuple quyền trực tiếp (tuple `parent` được thay bằng contextual tuples), nhưng thêm một
thành phần hạ tầng, việc migrate model, và ghi kép DB ↔ OpenFGA (outbox tuple). "Được chia sẻ với tôi" phục vụ từ Search.

Ngoài ra có hai việc mà chính sách "mọi service tự validate JWT của người dùng" không giải quyết được:
- Người được chia sẻ tải file của chủ sở hữu — Storage kiểm tra `blob.owner == user` của token;
- Khách mở link **ẩn danh** — không có token người dùng nào để chuyển tiếp.

**Quyết định**:
- **Sharing service** lưu `permissions` (người dùng × item × vai trò) và `share_links` (chỉ lưu SHA-256 của token;
  mật khẩu băm PBKDF2, kết quả đúng được nhớ 10 phút) trong PostgreSQL `sharing`. Chỉ có **gRPC nội bộ**
  (`sharing.v1.SharingService`): CheckAccess / BatchCheckAccess, List/Grant/Revoke, Create/List/Disable/ResolveLink.
  Mọi request mang `ItemRef {item, owner, ancestors}` ⇒ Sharing **không giữ bản sao cây thư mục**, kế thừa đúng ngay cả
  khi item vừa tạo / di chuyển; thu hồi có hiệu lực ở lần kiểm tra kế tiếp. Publish `ItemShared`, `ShareRoleChanged`,
  `ShareRevoked` qua outbox; consume `ItemsPurged`.
- **Drive là mặt tiền REST** cho chia sẻ (`/items/{id}/sharing|permissions|links`, `/public/links/{token}/...`): Drive đã
  nạp item, biết tên / chủ sở hữu / tổ tiên và đã kiểm tra quyền chia sẻ ⇒ không có phụ thuộc vòng Drive ↔ Sharing.
- **Định danh service**: Identity phát hành token OAuth 2.0 *client credentials* (`POST /connect/token`, ngoài `/api` nên
  Gateway không định tuyến tới) cùng khoá RS256 / JWKS, `sub = svc:{clientId}`, scope `mycloud.internal`. Drive gọi
  Storage / Sharing / Identity bằng token này (`AddGrpcServiceClient`), sau khi **chính Drive** đã quyết định quyền.
  Storage: token người dùng ⇒ `user_id` phải là chính họ (như trước); token service ⇒ tin `user_id` (chủ sở hữu). gRPC
  của Sharing và Identity chỉ nhận token service (policy `service`). Secret sinh bởi Aspire (parameter, user secrets).
- Chủ sở hữu trả dung lượng cho mọi thứ trong cây của mình (upload của người chỉnh sửa ⇒ blob của chủ, `uploader_id`
  = người upload để ký part); di chuyển chỉ trong cùng chủ sở hữu (sang Drive khác ⇒ sao chép).

**Hệ quả**: + Ít hạ tầng hơn, một nguồn sự thật, không có đồng bộ tuple; quyền kế thừa và thu hồi luôn tức thì. + Service
token mở đường cho job nền (sao chép thư mục lớn, zip — Phase 5). − Không có mô hình quan hệ khai báo (nhóm, domain,
"chia sẻ cho cả công ty"): khi cần sẽ đánh giá lại OpenFGA — interface `IItemAuthorizer` / gRPC của Sharing giữ nguyên.
− Drive được tin tưởng cao (token service vượt kiểm tra chủ sở hữu ở Storage): secret phải được bảo vệ, xoay vòng (Phase 6).

---

<a id="adr-015"></a>
## ADR-015 — Media Worker đi qua Storage; Storage nén zip từ hàng đợi trong PostgreSQL; nội dung người dùng không bao giờ được hiển thị như HTML

**Trạng thái**: Accepted (Phase 5).

**Bối cảnh**: Phase 5 thêm thumbnail / xem trước, tải nhiều item dạng zip và tải cả thư mục lên. Ba điểm cần quyết:
1. Media Worker đọc file gốc và ghi thumbnail thế nào: thiết kế ban đầu (docs/03 §8) cho Media đọc / ghi thẳng MinIO với
   key `thumbnails/{itemId}/{versionId}`. Như vậy Media phải biết cách Storage đặt tên object (`u/{owner}/{blob}`) và
   giữ khoá truy cập toàn bucket, còn Storage không biết thumbnail tồn tại nên không xoá được khi blob bị xoá.
2. Ai nén zip: Drive biết cây thư mục và tên, Storage giữ byte. Zip có thể tới vài GB và mất vài phút.
3. URL xem trực tiếp nằm **cùng origin** với web app (Vite / nginx proxy `/mycloud-blobs/` — ADR-013) và MIME do client
   khai báo lúc upload ⇒ mở inline một file HTML / SVG là stored XSS trên origin của MyCloud.

**Quyết định**:
- **Media Worker không có khoá object storage.** Nó dùng định danh service riêng (`media`, client credentials — ADR-014)
  để gọi Storage: `GetDownloadUrl(internal=true)` lấy presigned URL nội bộ để đọc (ffmpeg đọc thẳng URL bằng HTTP range,
  không tải cả video), `PutThumbnail` để ghi WebP 256 / 1024 px. Thumbnail gắn với **blob** (`t/{blobId}/{size}.webp`):
  Storage xoá cùng blob, không tính quota, ký URL không cần truy vấn DB. Media publish `ThumbnailGenerated`; Drive đánh
  dấu `thumbnail_version_id` chỉ khi version đó còn là version hiện tại (không tăng Revision, không phát event).
  Ảnh: SkiaSharp (giải mã có lấy mẫu, xoay theo EXIF, từ chối ảnh > 120 MP); PDF: PDFium (PDFtoImage); video: ffmpeg
  nếu có (không có ⇒ bỏ qua video). Nội dung hỏng ⇒ bỏ qua, không retry.
- **Drive dựng danh sách, Storage nén.** `POST /archives` (Drive) kiểm tra quyền xem, duyệt cây con, dựng đường dẫn trong
  zip rồi gọi `Storage.CreateArchive`. Storage ghi yêu cầu vào bảng `archives` (hàng đợi): worker nhận bằng UPDATE có
  điều kiện (Pending → Building, an toàn khi nhiều instance), stream từng object vào `ZipArchive` ghi lên một S3 multipart
  upload (bộ nhớ ~2 part, không cần đĩa tạm), xong thì publish `ArchiveReady` qua outbox. Tệp zip hết hạn sau 24 h, không
  tính quota; người dùng xem trạng thái / tải qua REST của Storage. Instance chết giữa chừng ⇒ job dọn dẹp trả yêu cầu về
  hàng đợi (tối đa 3 lần). Không dùng consumer MassTransit cho việc nén: một job vài phút không nên giữ transaction của
  inbox / outbox.
- **Không bao giờ hiển thị nội dung người dùng như trang web**: khi xem inline, Storage chỉ giữ nguyên kiểu với ảnh raster /
  video / audio / PDF; văn bản (kể cả HTML, JS, SVG, JSON) ép thành `text/plain; charset=utf-8`; kiểu khác chỉ được tải
  về. Lớp thứ hai: proxy (Vite, nginx) thêm `Content-Security-Policy: sandbox` và `X-Content-Type-Options: nosniff` cho
  mọi phản hồi của `/mycloud-blobs/`.
- **Tải cả thư mục lên**: Drive tạo cả cây thư mục trong một transaction (`POST /folders/tree`, gộp hoặc đổi tên khi
  trùng) trước khi upload file; client hỏi `POST /items/{id}/name-conflicts` một lần cho cả lô để người dùng chọn giữ
  cả hai / thay thế (file ⇒ phiên bản mới, thư mục ⇒ gộp) / bỏ qua.

**Hệ quả**: + Storage là nơi duy nhất giữ khoá và biết bố cục object; thumbnail và zip có vòng đời rõ ràng. + Zip lớn
không ảnh hưởng tới request của người dùng. − Media thêm một bước gRPC cho mỗi file (không đáng kể so với giải mã ảnh).
− Hàng đợi zip trong PostgreSQL chỉ phù hợp lượng yêu cầu vừa phải; nếu cần scale riêng thì tách worker nén thành service
(Phase 6+). − Entry > 4 GB trong zip (Zip64 trên stream không seek được) chưa được kiểm thử.

---

<a id="adr-016"></a>
## ADR-016 — Xem / tạo / sửa tài liệu Office ngay trong trình duyệt; quản trị viên bằng claim trong JWT

**Trạng thái**: Accepted (sau Phase 5).

**Bối cảnh**: người dùng cần đọc Word / Excel / văn bản, tạo tệp mới và sửa tệp ngay trong MyCloud, và cần một trang
quản trị người dùng. Sửa Office đúng nghĩa (giữ nguyên bố cục) cần một bộ soạn thảo Office riêng như OnlyOffice /
Collabora: container vài GB, license AGPL, giới hạn số người sửa cùng lúc ở bản community, tích hợp callback lưu tệp.

**Quyết định** (đã hỏi chủ dự án — chọn "trình sửa nhẹ"):
- **Mọi việc đọc / ghi nội dung Office diễn ra trong trình duyệt**, không thêm service: Word xem bằng docx-preview
  (giữ bố cục trang, bảng, ảnh), Excel / CSV bằng ExcelJS và lưới ô tự viết. Thư viện tải lười (chỉ khi mở). `.doc`,
  `.ppt(x)`: chỉ tải về.
- **Định dạng bảng tính nhận theo nội dung, không theo đuôi tệp**: zip ⇒ xlsx (chuẩn hoá trước khi đưa vào ExcelJS:
  bỏ tiền tố namespace `x:` và điền địa chỉ hàng / ô bị lược — tệp do OpenXML SDK / hệ thống báo cáo xuất ra); còn lại
  (.xls nhị phân BIFF, HTML, XML 2003) ⇒ SheetJS (Apache-2.0, cài từ CDN chính thức vì bản npm 0.18.5 có lỗi bảo mật đã
  biết), lưu lại thành .xls và chỉ giữ dữ liệu + công thức.
- **Sửa = lưu một phiên bản mới** qua luồng upload sẵn có (Drive không đọc byte của tệp — ADR-002): bản trước luôn còn
  trong lịch sử phiên bản nên mọi mất mát định dạng đều khôi phục được. Lưu kèm `expectedRevision` (revision lúc mở):
  người khác đã lưu sau đó ⇒ 412 `DRIVE.REVISION_MISMATCH`, trình sửa hỏi "Vẫn lưu" thay vì ghi đè âm thầm.
  - Văn bản / mã / Markdown / CSV: sửa đầy đủ, giữ BOM, kiểu xuống dòng, dấu phân cách CSV.
  - Excel: sửa giá trị / công thức ô ngay trên workbook đã nạp ⇒ định dạng, độ rộng cột, ô gộp, trang tính khác được
    giữ; công thức mới được Excel tính khi mở tệp (`fullCalcOnLoad`). `.xlsm` chỉ xem (ghi lại sẽ mất macro).
  - Word: mammoth ⇒ HTML có cấu trúc ⇒ TipTap ⇒ docx (tiêu đề, đậm / nghiêng / gạch, danh sách lồng / đánh số, trích
    dẫn, khối mã, bảng có gộp ô, ảnh PNG / JPEG / GIF / BMP, liên kết). Bố cục trang, đầu / chân trang, font, màu không
    giữ được — trình sửa ghi rõ điều này.
- **Tạo tệp mới** (Word, Excel, văn bản, Markdown): trình duyệt sinh nội dung trống rồi upload như tệp thường, sau đó mở
  trình sửa.
- **Quản trị viên**: cờ `is_admin` trong Identity ⇒ access token mang claim `role = admin`; mọi service tự kiểm tra bằng
  policy `admin` (BuildingBlocks) — không gọi Identity. Quản trị viên đầu tiên do cấu hình `Identity:AdminEmails` chỉ
  định (AppHost: parameter `admin-emails`). Trang quản trị: danh sách / tìm kiếm / lọc, khoá / mở khoá (thu hồi mọi
  refresh token, chặn đăng nhập — đăng nhập đúng mật khẩu mới thấy `IDENTITY.ACCOUNT_DISABLED`), cấp / thu quyền quản
  trị (không tự khoá / tự bỏ quyền mình), đổi hạn mức dung lượng (Storage), thống kê. Mọi thay đổi được ghi log Warning.

**Hệ quả**: + Không thêm hạ tầng; tệp Office mở nhanh, không rời trang. + Không bao giờ mất dữ liệu nhờ phiên bản. − Sửa
Word / Excel phức tạp có thể mất định dạng (đã cảnh báo, khôi phục được); nếu cần độ trung thực cao ⇒ tích hợp OnlyOffice /
Collabora sau (WOPI), giữ nguyên mô hình "lưu = phiên bản mới". − Thu quyền quản trị / khoá tài khoản có hiệu lực với
access token đang dùng khi token hết hạn (≤ 15 phút). − Chưa có nhật ký kiểm toán (audit log) lưu trong DB — Phase 6.

## ADR-017 — Lịch sử & trao đổi của tệp: ghi chú phiên bản, tin nhắn gắn phiên bản, phòng realtime qua SignalR

**Trạng thái**: Accepted (sau Phase 5).

**Bối cảnh**: nhiều người cùng làm trên một tệp cần biết lần trước ai đổi gì và vì sao, khôi phục bản cũ kèm lý do, và
trao đổi (kiểu chat) ngay cạnh tệp — góp ý một phiên bản cụ thể, nhắc tên người khác, thấy ngay khi người kia trả lời
hoặc vừa lưu bản mới. Chưa cần (và chưa đủ hạ tầng cho) cùng sửa đồng thời từng ký tự (CRDT / OT).

**Quyết định**:
- **Ghi chú phiên bản**: `file_versions.note` (≤ 1000 ký tự) nhập lúc lưu (`POST /uploads` `{note}`) hoặc lúc khôi phục
  (`POST /items/{id}/versions/{vid}/restore` `{note}`); sửa sau bằng `PATCH /items/{id}/versions/{vid}` — chỉ người tạo
  phiên bản hoặc chủ tệp.
- **Trao đổi** lưu ở Drive (cùng giao dịch với kiểm tra quyền, xoá vĩnh viễn tệp ⇒ xoá theo): bảng `item_comments(id,
  item_id, version_id?, parent_id?, author_id, body ≤ 4000, mentions uuid[], created_at, edited_at, deleted_at,
  resolved_at, resolved_by)`. Một cấp trả lời (trả lời của trả lời gắn vào luồng gốc, kế thừa phiên bản). Xoá mềm (giữ
  chỗ trong luồng). Luồng gốc có thể "đã xử lý". Phiên bản bị xoá ⇒ tin nhắn vẫn còn.
- **Quyền**: đọc = Viewer; viết = **Commenter** trở lên (vai trò "người nhận xét" từ Phase 4 nay có ý nghĩa); sửa tin =
  người viết; xoá = người viết hoặc chủ tệp; ghi chú phiên bản = người tạo phiên bản hoặc chủ tệp. `@nhắc tên` chỉ giữ
  người thực sự xem được tệp (chủ + người được chia sẻ trực tiếp hoặc kế thừa), người khác bị bỏ qua.
- **Người nhận thông báo** (event `ItemCommentPosted.Recipients`, Notification tạo thông báo loại `comment` + đẩy
  realtime): luồng mới ⇒ chủ tệp, người tạo phiên bản được góp ý, người được nhắc; trả lời ⇒ những người đã tham gia
  luồng + người được nhắc. Không bao giờ gửi cho chính người viết.
- **Phòng realtime theo tệp** trên hub `/hubs/notifications` sẵn có (một kết nối SignalR cho cả trang): Drive cấp vé
  `POST /items/{id}/live` = `LiveRoomToken` (HMAC-SHA256 của itemId + userId + hạn, khoá `Collaboration:Secret` dùng
  chung Drive ↔ Notification, hạn 12 giờ). Hub `Watch(ticket)` kiểm vé ⇒ vào nhóm `item:{id}`; Notification **không cần
  biết quyền** — Drive đã kiểm lúc cấp vé. Thu hồi chia sẻ có hiệu lực với phòng khi vé hết hạn hoặc khi tải lại trang.
  - Nội dung đẩy vào phòng chỉ là **tín hiệu** `item-activity {kind: comment | version | version_note, ...}` — client gọi
    lại REST (đã kiểm quyền) để lấy dữ liệu; không phát nội dung tin nhắn qua nhóm. Nguồn: `ItemCommentPosted`,
    `ItemHistoryChanged` (sửa / xoá / xử lý tin, sửa ghi chú), `FileVersionAdded`.
  - **Hiện diện**: client gọi `Presence(itemId, viewing | editing)` mỗi 20 giây; hub phát cho cả nhóm; không nhận tín hiệu
    sau 60 giây ⇒ coi như đã rời (không lưu trạng thái ở server ⇒ chạy được nhiều instance với Redis backplane). Trình sửa
    hiện cảnh báo "X cũng đang sửa tệp này" — kết hợp kiểm tra `expectedRevision` (412) của ADR-016 thay cho khoá tệp.
- **So sánh phiên bản ngay trong trình duyệt** (Drive vẫn không đọc nội dung tệp): văn bản / mã / CSV theo dòng (jsdiff);
  Word theo đoạn (mammoth → văn bản thuần); Excel theo từng ô (giá trị hoặc công thức) và trang tính thêm / bớt.
- **Liên kết trực tiếp** `/files/{id}?panel=history` (từ thông báo) mở xem trước kèm bảng "Lịch sử & trao đổi".

**Hệ quả**: + Không thêm service / hạ tầng; Notification vẫn không phụ thuộc Drive lúc chạy. + Mỗi thay đổi có người,
thời điểm, cách tạo, lý do; khôi phục không làm mất gì. − Chưa cùng sửa theo thời gian thực từng ký tự; hai người sửa cùng
lúc vẫn phải xử lý xung đột 412 (đã cảnh báo trước nhờ hiện diện). − So sánh Word bỏ qua định dạng; PDF / ảnh không so
sánh được. − Vé phòng không bị thu hồi tức thì khi bỏ chia sẻ (≤ 12 giờ, chỉ nhận tín hiệu, không nhận nội dung).
