# Tài liệu MyCloud

Ứng dụng lưu trữ đám mây kiểu Google Drive, kiến trúc microservice (.NET + React), các service giao tiếp bằng **gRPC** (đồng bộ) và **RabbitMQ** (bất đồng bộ).

| # | Tài liệu | Nội dung |
|---|---|---|
| 01 | [Hiện trạng](01-hien-trang.md) | Đánh giá code hiện có, điểm tốt, danh sách lỗi/rủi ro |
| 02 | [Kiến trúc tổng thể](02-kien-truc-tong-the.md) | Mục tiêu sản phẩm, NFR, sơ đồ, danh sách service, công nghệ, cấu trúc repo |
| 03 | [Đặc tả service](03-dac-ta-service.md) | Trách nhiệm, dữ liệu, REST, gRPC, event của từng service |
| 04 | [gRPC & Message Queue](04-giao-tiep-grpc-va-event.md) | Proto, danh mục event, outbox/inbox, retry, idempotency, lan truyền user |
| 05 | [Luồng nghiệp vụ](05-luong-nghiep-vu.md) | Sequence diagram: đăng ký, upload, download, di chuyển, thùng rác, chia sẻ, thumbnail |
| 06 | [Hạ tầng, bảo mật, vận hành](06-ha-tang-bao-mat-van-hanh.md) | Aspire/compose, secret, bảo mật, observability, test, CI/CD, triển khai |
| 07 | [Kế hoạch thực hiện](07-ke-hoach-thuc-hien.md) | 7 phase, checklist, tiêu chí hoàn thành, mapping code cũ → mới, rủi ro |
| 08 | [ADR](08-adr.md) | Các quyết định kiến trúc và lý do |

**Đọc nhanh**: 01 → 02 → 07. Khi bắt tay code một service: 03 + 04 + 05.

Sơ đồ dùng Mermaid — xem trực tiếp trên GitHub hoặc VS Code (extension *Markdown Preview Mermaid Support*).
