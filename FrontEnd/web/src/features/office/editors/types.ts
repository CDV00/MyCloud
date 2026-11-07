/** Hợp đồng giữa khung trình sửa (lưu, phím tắt, cảnh báo) và từng loại trình sửa. */
export interface EditorProps {
  /** Nội dung hiện tại của file (version mới nhất lúc mở). */
  bytes: ArrayBuffer;
  name: string;
  /** Báo nội dung đã thay đổi (chưa lưu). */
  onDirty(): void;
  /** Đăng ký hàm tạo nội dung để lưu (gọi khi người dùng bấm Lưu / Ctrl+S). */
  register(serialize: () => Promise<Blob>): void;
}
