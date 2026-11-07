import type { DriveItem } from '../../api/drive';
import type { Category, SearchHit } from '../../api/search';

/**
 * Kết quả của Search là bản chiếu rút gọn (không có revision, trạng thái sao…). Đổi sang dạng DriveItem để hiển thị
 * bằng cùng bảng; các thao tác cần dữ liệu đầy đủ (đổi tên, gắn sao) làm từ panel chi tiết hoặc trang thư mục.
 */
export function hitToItem(hit: SearchHit, role: DriveItem['role'] = 'viewer'): DriveItem {
  return {
    id: hit.id,
    parentId: hit.parentId,
    kind: hit.kind,
    name: hit.name,
    mimeType: hit.mimeType,
    sizeBytes: hit.sizeBytes,
    isRoot: false,
    revision: 0,
    createdAt: hit.modifiedAt,
    modifiedAt: hit.modifiedAt,
    starred: false,
    trashedAt: null,
    versionNo: 0,
    ownerId: hit.ownerId,
    modifiedBy: null,
    // Vai trò thật được Drive tính khi mở item; ở danh sách Search chỉ cần biết có phải của mình không.
    role,
  };
}

export const categoryLabels: Record<Category, string> = {
  folder: 'Thư mục',
  image: 'Hình ảnh',
  video: 'Video',
  audio: 'Âm thanh',
  pdf: 'PDF',
  doc: 'Tài liệu',
  sheet: 'Bảng tính',
  slide: 'Bản trình bày',
  archive: 'Tệp nén',
  other: 'Khác',
};

export type ModifiedRange = 'any' | 'today' | '7d' | '30d' | 'year';

export const modifiedLabels: Record<ModifiedRange, string> = {
  any: 'Mọi lúc',
  today: 'Hôm nay',
  '7d': '7 ngày qua',
  '30d': '30 ngày qua',
  year: 'Năm nay',
};

/** Mốc "sửa đổi từ" (ISO) cho một khoảng thời gian; null = không lọc. */
export function modifiedAfter(range: ModifiedRange, now = new Date()): string | null {
  const date = new Date(now);
  switch (range) {
    case 'today':
      date.setHours(0, 0, 0, 0);
      return date.toISOString();
    case '7d':
      return new Date(now.getTime() - 7 * 86_400_000).toISOString();
    case '30d':
      return new Date(now.getTime() - 30 * 86_400_000).toISOString();
    case 'year':
      return new Date(now.getFullYear(), 0, 1).toISOString();
    default:
      return null;
  }
}
