import { apiFetch } from './http';

// Notification service (docs/03 §7): thông báo trong app + realtime qua SignalR.

export interface ItemSharedPayload {
  itemId: string;
  itemName: string;
  itemKind: 'folder' | 'file';
  role: 'viewer' | 'commenter' | 'editor';
  sharedBy: string;
  sharedByName: string;
  message: string | null;
}

export interface QuotaPayload {
  percent: number;
  usedBytes: number;
  limitBytes: number;
}

export interface ArchiveReadyPayload {
  archiveId: string;
  name: string;
  sizeBytes: number;
  fileCount: number;
  skippedCount: number;
  expiresAt: string;
}

export interface ArchiveFailedPayload {
  archiveId: string;
  name: string;
  reason: string;
}

export interface CommentPayload {
  itemId: string;
  itemName: string;
  commentId: string;
  versionId: string | null;
  versionNo: number | null;
  isReply: boolean;
  mentioned: boolean;
  authorId: string;
  authorName: string;
  excerpt: string;
}

interface Base {
  id: string;
  createdAt: string;
  read: boolean;
}

export type AppNotification = Base &
  (
    | { type: 'item_shared'; payload: ItemSharedPayload }
    | { type: 'quota'; payload: QuotaPayload }
    | { type: 'archive_ready'; payload: ArchiveReadyPayload }
    | { type: 'archive_failed'; payload: ArchiveFailedPayload }
    | { type: 'comment'; payload: CommentPayload }
  );

export interface NotificationList {
  items: AppNotification[];
  unreadCount: number;
  nextBefore: string | null;
}

const NOTIFICATIONS = '/api/notification/v1/notifications';

/** Hub SignalR (qua Gateway; token đi trong query access_token vì WebSocket không gắn được header). */
export const NOTIFICATION_HUB = '/api/notification/hubs/notifications';

export const listNotifications = () => apiFetch<NotificationList>(NOTIFICATIONS);

export const markNotificationRead = (id: string) =>
  apiFetch(`${NOTIFICATIONS}/${id}/read`, { method: 'POST' });

export const markAllNotificationsRead = () => apiFetch(`${NOTIFICATIONS}/read-all`, { method: 'POST' });
