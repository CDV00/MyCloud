import { getDownloadUrl, type DriveItem } from '../../api/drive';

/** Đường dẫn trang của một thư mục. */
export function folderPath(item: Pick<DriveItem, 'id' | 'isRoot'>) {
  return item.isRoot ? '/my-drive' : `/my-drive/folders/${item.id}`;
}

/**
 * Tải file: lấy presigned URL (trình duyệt không gắn được header Authorization khi điều hướng) rồi mở nó.
 * Object storage trả Content-Disposition: attachment kèm tên file gốc nên trình duyệt tải về, không rời trang.
 */
export async function downloadItem(itemId: string): Promise<void> {
  const { url } = await getDownloadUrl(itemId);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.rel = 'noopener';
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
}
