import { apiFetch } from './http';

// Identity: tên hiển thị theo id (chủ sở hữu, người chia sẻ, người thực hiện hoạt động).

export interface UserSummary {
  id: string;
  displayName: string;
  email: string;
  avatarUrl: string | null;
}

export const getUsers = (ids: string[]) =>
  apiFetch<UserSummary[]>(`/api/identity/v1/users?ids=${ids.map(encodeURIComponent).join(',')}`);
