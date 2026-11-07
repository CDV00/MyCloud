import type { DriveItem } from '../../api/drive';
import type { ActivityEntry } from '../../api/search';

/** Tên loại dễ đọc cho panel chi tiết. */
export function describeType(item: Pick<DriveItem, 'kind' | 'mimeType'>): string {
  if (item.kind === 'folder') return 'Thư mục';
  const mime = item.mimeType ?? '';
  if (mime.startsWith('image/')) return 'Hình ảnh';
  if (mime.startsWith('video/')) return 'Video';
  if (mime.startsWith('audio/')) return 'Âm thanh';
  if (mime === 'application/pdf') return 'PDF';
  if (/spreadsheet|excel|csv/.test(mime)) return 'Bảng tính';
  if (/presentation|powerpoint/.test(mime)) return 'Bản trình bày';
  if (/word|opendocument\.text|rtf/.test(mime)) return 'Tài liệu';
  if (/zip|compressed|tar|rar|7z|gzip/.test(mime)) return 'Tệp nén';
  if (mime.startsWith('text/')) return 'Văn bản';
  return mime || 'Tệp';
}

/** Ngày giờ đầy đủ: "07/11/2026, 14:05". */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

const ROOT_LABEL = 'Drive của tôi';

/** Một dòng hoạt động: "Bạn đã đổi tên “a” thành “b”". */
export function describeActivity(
  entry: ActivityEntry,
  currentUserId: string | undefined,
  actorName?: string,
): string {
  const actor = entry.actorId === currentUserId ? 'Bạn' : (actorName ?? 'Người khác');
  const what = entry.kind === 'folder' ? 'thư mục' : 'tệp';
  const name = `“${entry.name}”`;
  switch (entry.action) {
    case 'created':
      return `${actor} đã tạo ${what} ${name}`;
    case 'renamed':
      return entry.previousName
        ? `${actor} đã đổi tên “${entry.previousName}” thành ${name}`
        : `${actor} đã đổi tên thành ${name}`;
    case 'moved':
      return `${actor} đã di chuyển ${name} từ “${entry.fromFolder ?? ROOT_LABEL}” tới “${entry.toFolder ?? ROOT_LABEL}”`;
    case 'trashed':
      return `${actor} đã chuyển ${name} vào thùng rác`;
    case 'restored':
      return `${actor} đã khôi phục ${name}`;
    case 'edited':
      return `${actor} đã chỉnh sửa ${name}${entry.versionNo ? ` (phiên bản ${entry.versionNo})` : ''}`;
    case 'version_restored':
      return `${actor} đã khôi phục ${name} về phiên bản ${entry.restoredFromVersionNo ?? '?'}${
        entry.versionNo ? ` (thành phiên bản ${entry.versionNo})` : ''
      }`;
    case 'version_added':
      return `${actor} đã tải lên phiên bản ${entry.versionNo ?? ''} của ${name}`.replace('  ', ' ');
    default:
      return `${actor} đã thay đổi ${name}`;
  }
}
