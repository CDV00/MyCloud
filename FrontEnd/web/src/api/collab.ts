import { apiFetch } from './http';

// Lịch sử & trao đổi của tệp (ADR-017): tin nhắn chung / góp ý phiên bản, ghi chú phiên bản, vé vào phòng realtime.

export interface FileComment {
  id: string;
  itemId: string;
  /** Phiên bản được góp ý; null = trao đổi chung. */
  versionId: string | null;
  /** Null nếu trao đổi chung hoặc phiên bản đã bị xoá. */
  versionNo: number | null;
  /** Luồng gốc (null nếu chính nó là gốc). */
  parentId: string | null;
  authorId: string;
  /** Null nếu đã bị xoá. */
  body: string | null;
  mentions: string[];
  createdAt: string;
  editedAt: string | null;
  deleted: boolean;
  resolvedAt: string | null;
  resolvedBy: string | null;
}

export interface LiveTicket {
  token: string;
  expiresAt: string;
}

const items = (id: string) => `/api/drive/v1/items/${id}`;

export const listComments = (itemId: string) => apiFetch<FileComment[]>(`${items(itemId)}/comments`);

export const postComment = (
  itemId: string,
  body: { body: string; versionId?: string | null; parentId?: string | null; mentions?: string[] },
) => apiFetch<FileComment>(`${items(itemId)}/comments`, { method: 'POST', json: body });

export const editComment = (itemId: string, commentId: string, body: string) =>
  apiFetch<FileComment>(`${items(itemId)}/comments/${commentId}`, { method: 'PATCH', json: { body } });

export const deleteComment = (itemId: string, commentId: string) =>
  apiFetch(`${items(itemId)}/comments/${commentId}`, { method: 'DELETE' });

export const resolveComment = (itemId: string, commentId: string, resolved: boolean) =>
  apiFetch<FileComment>(`${items(itemId)}/comments/${commentId}/resolve`, {
    method: 'POST',
    json: { resolved },
  });

export const updateVersionNote = (itemId: string, versionId: string, note: string) =>
  apiFetch(`${items(itemId)}/versions/${versionId}`, {
    method: 'PATCH',
    json: { note: note.trim() || null },
  });

export const issueLiveTicket = (itemId: string) =>
  apiFetch<LiveTicket>(`${items(itemId)}/live`, { method: 'POST' });
