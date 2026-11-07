# 05 — Các luồng nghiệp vụ chính

## 1. Đăng ký & khởi tạo tài khoản

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant G as Gateway
    participant I as Identity
    participant MQ as RabbitMQ
    participant D as Drive
    participant ST as Storage
    participant N as Notification
    C->>G: POST /api/identity/v1/auth/register
    G->>I: forward (route công khai, rate-limit)
    I->>I: Tạo user + outbox(UserRegistered) — 1 transaction
    I-->>C: 201 Created
    I--)MQ: UserRegistered
    par
        MQ--)D: tạo item gốc "Drive của tôi"
    and
        MQ--)ST: tạo quota 15 GB
    and
        MQ--)N: lưu liên hệ + email chào mừng
    end
```

Client đăng nhập ngay sau đó có thể gặp trường hợp root chưa kịp tạo (vài trăm ms). `GET /items/root` của Drive xử lý **tạo-nếu-chưa-có** (idempotent, cùng logic với consumer) ⇒ không bao giờ lỗi.

## 2. Đăng nhập & làm mới token

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant I as Identity
    C->>I: POST /auth/login {email, password}
    I->>I: Kiểm tra lockout, password hash
    I-->>C: accessToken (15') + refreshToken (30 ngày, lưu hash)
    Note over C: access token giữ trong bộ nhớ<br/>refresh token trong cookie HttpOnly, Secure, SameSite=Strict, Path=/api/identity
    C->>I: POST /auth/refresh (cookie)
    I->>I: Token hợp lệ? → đánh dấu đã dùng, phát token mới cùng family
    alt token đã dùng lại (bị đánh cắp)
        I->>I: Thu hồi toàn bộ family
        I-->>C: 401
    else hợp lệ
        I-->>C: cặp token mới
    end
```

## 3. Upload file (multipart, resume được)

```mermaid
sequenceDiagram
    autonumber
    participant C as Client (Uppy)
    participant D as Drive
    participant SH as Sharing
    participant ST as Storage
    participant O as MinIO
    participant MQ as RabbitMQ

    C->>D: POST /uploads {parentId, name, size, mime}
    D->>D: Parent tồn tại, chưa ở thùng rác, tên hợp lệ
    opt người gọi không phải chủ thư mục
        D->>SH: gRPC Check(user, parentId, EDIT, ancestors)
    end
    D->>ST: gRPC CreateUploadSession(user, size, mime)
    ST->>ST: Reserve quota (UPDATE ... WHERE used+reserved+size <= limit)
    ST->>O: CreateMultipartUpload
    ST-->>D: sessionId, blobId, partSize, URL các part đầu
    D->>D: Lưu pending_upload(sessionId, parentId, name)
    D-->>C: uploadId + URL part

    loop từng part (song song 3–4)
        C->>O: PUT part (presigned URL)
        O-->>C: ETag
    end
    Note over C,O: Mất mạng → client hỏi lại các part đã có,<br/>xin URL mới qua GET /api/storage/v1/uploads/{id}/parts

    C->>D: POST /uploads/{id}:complete {parts[etag]}
    D->>ST: gRPC CompleteUpload
    ST->>O: CompleteMultipartUpload + HEAD (kiểm tra size)
    ST->>ST: Commit quota, blob → Uploaded
    ST-->>D: blobId, size
    D->>D: Transaction: tạo DriveItem + FileVersion v1<br/>+ outbox(ItemCreated, FileVersionAdded)
    D-->>C: 201 item
    D--)MQ: ItemCreated, FileVersionAdded
    MQ--)ST: claim blob (Uploaded → Claimed)
    MQ--)SH: ghi tuple owner/parent
    Note over MQ: Search index item, Media Worker sinh thumbnail
```

**Xử lý lỗi**

| Sự cố | Kết quả |
|---|---|
| Hết quota ở bước reserve | `CreateUploadSession` trả `RESOURCE_EXHAUSTED` → Drive trả `409` problem `STORAGE.QUOTA_EXCEEDED` |
| Client bỏ dở | Session hết hạn (24 h) → Storage abort multipart, trả quota |
| Storage complete OK nhưng Drive lỗi khi ghi DB | Blob ở trạng thái `Uploaded` không ai claim → GC sau 24 h xoá + trả quota. Client retry `:complete` → Drive gọi lại `CompleteUpload` (idempotent theo sessionId) |
| Size thực tế ≠ size khai báo | Storage từ chối, xoá object, trả quota |
| Trùng tên trong thư mục | Drive tự đổi tên `name (1).ext` (hoặc tạo version mới nếu `targetItemId` được gửi) |

## 4. Tải file / xem trước

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant D as Drive
    participant SH as Sharing
    participant ST as Storage
    participant O as MinIO
    C->>D: POST /items/{id}:downloadUrl
    D->>D: Item là file, không ở thùng rác, không bị đánh dấu malware
    opt không phải chủ sở hữu
        D->>SH: gRPC Check(user, id, VIEW, ancestors)
    end
    D->>ST: gRPC GetDownloadUrl(blobId, fileName, ttl=300)
    ST-->>D: presigned URL
    D--)D: outbox(ItemAccessed) — đã gộp theo 5 phút
    D-->>C: {url, expiresAt}
    C->>O: GET url (trình duyệt tải trực tiếp)
```

## 5. Di chuyển thư mục

```mermaid
sequenceDiagram
    autonumber
    participant C as Client
    participant D as Drive
    participant SH as Sharing
    participant MQ as RabbitMQ
    participant SR as Search
    C->>D: POST /items/{id}:move {newParentId} (If-Match: revision)
    D->>SH: BatchCheck: EDIT trên item và trên thư mục đích (nếu không phải chủ)
    D->>D: Kiểm tra: đích không phải con cháu của item, không trùng tên
    D->>D: UPDATE path của cả cây con (1 câu SQL)<br/>Revision++ + outbox(ItemMoved)
    D-->>C: 200
    D--)MQ: ItemMoved
    MQ--)SH: xoá tuple parent cũ, ghi parent mới
    MQ--)SR: cập nhật parentId/ancestorIds (cả cây con)
```

## 6. Thùng rác → xoá vĩnh viễn

```mermaid
stateDiagram-v2
    [*] --> Active
    Active --> Trashed: trash
    Trashed --> Active: restore
    Trashed --> Purged: xoá vĩnh viễn hoặc sau 30 ngày
    Purged --> [*]: ItemsPurged
```

| Chuyển trạng thái | Chi tiết |
|---|---|
| `:trash` | Đánh dấu item và toàn bộ con cháu (`trash_root_id = item`) |
| `:restore` | Khôi phục cả nhóm có cùng `trash_root_id`; nếu thư mục cha đã bị xoá → khôi phục về root |
| Purge | `DELETE /items/{id}` hoặc job tự động sau 30 ngày |
| `ItemsPurged` | Storage xoá blob & trả quota, Sharing xoá quyền/tuple, Search xoá index |

- File trong thùng rác **vẫn tính quota** (giống Google Drive).
- Purge cây lớn: Drive xoá theo lô 500 item, mỗi lô 1 event `ItemsPurged`.

## 7. Chia sẻ

```mermaid
sequenceDiagram
    autonumber
    participant C as Client (chủ)
    participant SH as Sharing
    participant I as Identity
    participant F as OpenFGA
    participant MQ as RabbitMQ
    participant N as Notification
    participant SR as Search
    C->>SH: POST /items/{id}/permissions {email, role: editor}
    SH->>F: Check(user, can_share, item)
    SH->>I: gRPC FindUserByEmail
    I-->>SH: user B
    SH->>SH: Transaction: permissions + fga_tuple_outbox + outbox(ItemShared)
    SH-->>C: 201
    SH->>F: Write tuple (item:{id}#editor@user:B) — từ fga_tuple_outbox
    SH--)MQ: ItemShared
    MQ--)N: email + realtime cho B
    MQ--)SR: access_grants(B, item) → xuất hiện trong "Được chia sẻ với tôi"
```

Chia sẻ qua link: `POST /items/{id}/links` → token ngẫu nhiên (chỉ trả 1 lần, lưu hash), tuple `item:{id}#viewer@link:{linkId}`. Người truy cập mở `https://app/s/{token}` → `GET /api/sharing/v1/links/{token}` (anonymous) → client gọi Drive kèm header `X-Share-Token`; Drive kiểm tra bằng `Check(subject = link_token)`.

## 8. Tạo thumbnail

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant M as Media Worker
    participant O as MinIO
    participant D as Drive
    MQ--)M: FileVersionAdded (image/*, video/*, application/pdf)
    M->>O: GET object (nội bộ)
    M->>M: Sinh WebP 256 & 1024
    M->>O: PUT thumbnails/{itemId}/{versionId}/{size}.webp
    M--)MQ: ThumbnailGenerated
    MQ--)D: HasThumbnail = true
```
