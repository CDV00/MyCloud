import { apiFetch } from './http';

// Trang quản trị người dùng (chỉ quản trị viên — claim role=admin trong access token).

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  createdAt: string;
  lastLoginAt: string | null;
  isAdmin: boolean;
  disabled: boolean;
  disabledAt: string | null;
}

export interface AdminUserPage {
  items: AdminUser[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AdminSummary {
  users: number;
  admins: number;
  disabled: number;
  activeLast30Days: number;
}

export interface AdminQuota {
  userId: string;
  limitBytes: number;
  usedBytes: number;
  reservedBytes: number;
}

export interface AdminStorageSummary {
  usedBytes: number;
  limitBytes: number;
  blobs: number;
  defaultQuotaBytes: number;
}

export type UserStatusFilter = 'all' | 'active' | 'disabled' | 'admin';

const IDENTITY = '/api/identity/v1/admin';
const STORAGE = '/api/storage/v1/admin';

export const getAdminSummary = () => apiFetch<AdminSummary>(`${IDENTITY}/summary`);

export function listUsers(q: string, status: UserStatusFilter, page: number, pageSize: number) {
  const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (q.trim()) query.set('q', q.trim());
  if (status !== 'all') query.set('status', status);
  return apiFetch<AdminUserPage>(`${IDENTITY}/users?${query}`);
}

export const updateUser = (id: string, patch: { isAdmin?: boolean; disabled?: boolean }) =>
  apiFetch<AdminUser>(`${IDENTITY}/users/${id}`, { method: 'PATCH', json: patch });

export const getStorageSummary = () => apiFetch<AdminStorageSummary>(`${STORAGE}/summary`);

export const getQuotas = (userIds: string[]) =>
  userIds.length === 0
    ? Promise.resolve([] as AdminQuota[])
    : apiFetch<AdminQuota[]>(`${STORAGE}/quotas?userIds=${userIds.join(',')}`);

export const setQuota = (userId: string, limitBytes: number) =>
  apiFetch<AdminQuota>(`${STORAGE}/quotas/${userId}`, { method: 'PUT', json: { limitBytes } });
