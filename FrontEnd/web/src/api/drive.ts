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
  starred: boolean;
  /** Có giá trị ⇒ item đang ở thùng rác. */
  trashedAt: string | null;
  /** Số version hiện tại (file); 0 với thư mục. */
  versionNo: number;
  ownerId: string;
  modifiedBy: string | null;
  /** Vai trò của người đang xem: quyết định thao tác nào được hiện. */
  role: Role;
}

export type Role = 'owner' | 'editor' | 'commenter' | 'viewer' | 'none';

/** Sửa được (đổi tên, di chuyển, tải lên bên trong, version). */
export const canEdit = (item: Pick<DriveItem, 'role'>) => item.role === 'owner' || item.role === 'editor';

/** Thùng rác / xoá vĩnh viễn: chỉ chủ sở hữu. */
export const canManage = (item: Pick<DriveItem, 'role'>) => item.role === 'owner';

export interface FileVersion {
  id: string;
  versionNo: number;
  sizeBytes: number;
  mimeType: string;
  createdAt: string;
  createdBy: string;
  isCurrent: boolean;
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

export const getDownloadUrl = (id: string, options: { inline?: boolean; versionId?: string } = {}) =>
  apiFetch<{ url: string; expiresAt: string }>(`${DRIVE}/items/${id}/download-url`, {
    method: 'POST',
    json: { inline: options.inline ?? false, versionId: options.versionId ?? null },
  });

/** Nơi file tải lên sẽ vào: thư mục (file mới) hoặc file có sẵn (phiên bản mới). */
export type UploadTarget = { parentId: string } | { targetItemId: string };

export const startUpload = (target: UploadTarget, file: File, signal?: AbortSignal) =>
  apiFetch<UploadSession>(`${DRIVE}/uploads`, {
    method: 'POST',
    json: { ...target, name: file.name, sizeBytes: file.size, mimeType: file.type || null },
    signal,
  });

export const completeUpload = (uploadId: string, parts: { partNumber: number; eTag: string }[]) =>
  apiFetch<DriveItem>(`${DRIVE}/uploads/${uploadId}/complete`, { method: 'POST', json: { parts } });

export const abortUpload = (uploadId: string) =>
  apiFetch(`${DRIVE}/uploads/${uploadId}`, { method: 'DELETE' });

// --- Thùng rác (docs/05 §6) ---

export const trashItem = (id: string) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${id}/trash`, { method: 'POST' });

export const restoreItem = (id: string) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${id}/restore`, { method: 'POST' });

/** Xoá vĩnh viễn (chỉ item đang ở thùng rác). */
export const purgeItem = (id: string) => apiFetch(`${DRIVE}/items/${id}`, { method: 'DELETE' });

export const listTrash = (pageToken?: string | null) =>
  apiFetch<ItemPage>(`${DRIVE}/trash${pageToken ? `?pageToken=${encodeURIComponent(pageToken)}` : ''}`);

export const emptyTrash = () => apiFetch<{ purged: number }>(`${DRIVE}/trash`, { method: 'DELETE' });

// --- Gắn sao, sao chép ---

export const setStar = (id: string, starred: boolean) =>
  apiFetch(`${DRIVE}/items/${id}/star`, { method: starred ? 'PUT' : 'DELETE' });

export const listStarred = (pageToken?: string | null) =>
  apiFetch<ItemPage>(`${DRIVE}/starred${pageToken ? `?pageToken=${encodeURIComponent(pageToken)}` : ''}`);

export const copyItem = (id: string, options: { targetParentId?: string; name?: string } = {}) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${id}/copy`, { method: 'POST', json: options });

// --- Version ---

export const listVersions = (id: string) => apiFetch<FileVersion[]>(`${DRIVE}/items/${id}/versions`);

export const restoreVersion = (id: string, versionId: string) =>
  apiFetch<DriveItem>(`${DRIVE}/items/${id}/versions/${versionId}/restore`, { method: 'POST' });

export const deleteVersion = (id: string, versionId: string) =>
  apiFetch(`${DRIVE}/items/${id}/versions/${versionId}`, { method: 'DELETE' });

// --- Chia sẻ (Drive là mặt tiền, Sharing service lưu quyền — ADR-014) ---

export type GrantRole = 'viewer' | 'commenter' | 'editor';

export interface Person {
  id: string;
  displayName: string;
  email: string;
  avatarUrl: string | null;
}

export interface SharePermission {
  id: string;
  user: Person;
  role: GrantRole;
  /** Có giá trị ⇒ kế thừa từ thư mục này (đổi / thu hồi ở thư mục đó). */
  inheritedFrom: { id: string; name: string } | null;
}

export interface ShareLink {
  id: string;
  role: GrantRole;
  hasPassword: boolean;
  expiresAt: string | null;
  createdAt: string;
}

export interface SharingInfo {
  owner: Person | null;
  myRole: Role;
  canShare: boolean;
  permissions: SharePermission[];
  links: ShareLink[];
}

export const getSharing = (id: string) => apiFetch<SharingInfo>(`${DRIVE}/items/${id}/sharing`);

export const shareItem = (
  id: string,
  body: { email: string; role: GrantRole; notify: boolean; message?: string },
) => apiFetch<SharePermission>(`${DRIVE}/items/${id}/permissions`, { method: 'POST', json: body });

export const changePermission = (id: string, permissionId: string, role: GrantRole) =>
  apiFetch(`${DRIVE}/items/${id}/permissions/${permissionId}`, { method: 'PATCH', json: { role } });

export const revokePermission = (id: string, permissionId: string) =>
  apiFetch(`${DRIVE}/items/${id}/permissions/${permissionId}`, { method: 'DELETE' });

export const createLink = (
  id: string,
  body: { role: GrantRole; password?: string; expiresAt?: string | null },
) =>
  apiFetch<{ link: ShareLink; token: string }>(`${DRIVE}/items/${id}/links`, { method: 'POST', json: body });

export const disableLink = (id: string, linkId: string) =>
  apiFetch(`${DRIVE}/items/${id}/links/${linkId}`, { method: 'DELETE' });

/** Địa chỉ trang mở link (cùng origin với web app). */
export const linkUrl = (token: string) => `${window.location.origin}/s/${token}`;

// --- Truy cập qua link (ẩn danh được; mật khẩu trong header, không trên URL) ---

const publicLink = (token: string) => `${DRIVE}/public/links/${encodeURIComponent(token)}`;

const linkHeaders = (password?: string | null) => (password ? { 'X-Link-Password': password } : undefined);

export const openLink = (token: string, password?: string | null) =>
  apiFetch<DriveItem>(publicLink(token), { headers: linkHeaders(password) });

export const getLinkedItem = (token: string, id: string, password?: string | null) =>
  apiFetch<DriveItem>(`${publicLink(token)}/items/${id}`, { headers: linkHeaders(password) });

export const listLinkedChildren = (
  token: string,
  id: string,
  password?: string | null,
  pageToken?: string | null,
) =>
  apiFetch<ItemPage>(
    `${publicLink(token)}/items/${id}/children${pageToken ? `?pageToken=${encodeURIComponent(pageToken)}` : ''}`,
    { headers: linkHeaders(password) },
  );

export const getLinkedAncestors = (token: string, id: string, password?: string | null) =>
  apiFetch<DriveItem[]>(`${publicLink(token)}/items/${id}/ancestors`, { headers: linkHeaders(password) });

export const getLinkedDownloadUrl = (token: string, id: string, password?: string | null) =>
  apiFetch<{ url: string; expiresAt: string }>(`${publicLink(token)}/items/${id}/download-url`, {
    method: 'POST',
    json: { inline: false },
    headers: linkHeaders(password),
  });
