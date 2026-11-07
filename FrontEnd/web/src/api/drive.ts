import { apiFetch } from './http';

// Drive service (docs/03 §3): cây thư mục đọc thẳng PostgreSQL nên thấy ngay thứ vừa tạo (ADR-003).

export type ItemKind = 'folder' | 'file';

export interface DriveItem {
  id: string;
  parentId: string | null;
  kind: ItemKind;
  name: string;
  mimeType: string | null;
  sizeBytes: number;
  isRoot: boolean;
  revision: number;
  createdAt: string;
  modifiedAt: string;
}

export interface ItemPage {
  items: DriveItem[];
  nextPageToken: string | null;
}

export type SortField = 'name' | 'modifiedAt' | 'size';

export interface PresignedPart {
  partNumber: number;
  url: string;
}

export interface UploadSession {
  uploadId: string;
  partSize: number;
  partCount: number;
  parts: PresignedPart[];
  expiresAt: string;
}

const DRIVE = '/api/drive/v1';

export const getRoot = () => apiFetch<DriveItem>(`${DRIVE}/items/root`);

export const getItem = (id: string) => apiFetch<DriveItem>(`${DRIVE}/items/${id}`);

export const getAncestors = (id: string) => apiFetch<DriveItem[]>(`${DRIVE}/items/${id}/ancestors`);

export function listChildren(
  folderId: string,
  sort: SortField,
  desc: boolean,
  pageToken?: string | null,
  pageSize = 100,
): Promise<ItemPage> {
  const query = new URLSearchParams({ orderBy: sort, desc: String(desc), pageSize: String(pageSize) });
  if (pageToken) query.set('pageToken', pageToken);
  return apiFetch<ItemPage>(`${DRIVE}/items/${folderId}/children?${query}`);
}

export const createFolder = (parentId: string, name: string) =>
  apiFetch<DriveItem>(`${DRIVE}/folders`, { method: 'POST', json: { parentId, name } });

/** Đổi tên với If-Match: người khác vừa sửa ⇒ 412 thay vì ghi đè âm thầm. */
export const renameItem = (item: DriveItem, name: string) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${item.id}`, {
    method: 'PATCH',
    json: { name },
    headers: { 'If-Match': `"${item.revision}"` },
  });

export const moveItem = (item: DriveItem, newParentId: string) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${item.id}/move`, {
    method: 'POST',
    json: { newParentId },
    headers: { 'If-Match': `"${item.revision}"` },
  });

export const getDownloadUrl = (id: string, inline = false) =>
  apiFetch<{ url: string; expiresAt: string }>(`${DRIVE}/items/${id}/download-url`, {
    method: 'POST',
    json: { inline },
  });

export const startUpload = (parentId: string, file: File, signal?: AbortSignal) =>
  apiFetch<UploadSession>(`${DRIVE}/uploads`, {
    method: 'POST',
    json: { parentId, name: file.name, sizeBytes: file.size, mimeType: file.type || null },
    signal,
  });

export const completeUpload = (uploadId: string, parts: { partNumber: number; eTag: string }[]) =>
  apiFetch<DriveItem>(`${DRIVE}/uploads/${uploadId}/complete`, { method: 'POST', json: { parts } });

export const abortUpload = (uploadId: string) =>
  apiFetch(`${DRIVE}/uploads/${uploadId}`, { method: 'DELETE' });
