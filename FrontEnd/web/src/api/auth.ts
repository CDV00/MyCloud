import { apiFetch, AUTH_BASE, setAccessToken, type TokenResponse } from './http';

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  avatarUrl: string | null;
  createdAt: string;
}

export async function login(email: string, password: string): Promise<void> {
  const tokens = await apiFetch<TokenResponse>(`${AUTH_BASE}/login`, {
    method: 'POST',
    json: { email, password },
    anonymous: true,
  });
  setAccessToken(tokens.accessToken);
}

export async function register(email: string, password: string, displayName: string): Promise<void> {
  await apiFetch(`${AUTH_BASE}/register`, {
    method: 'POST',
    json: { email, password, displayName },
    anonymous: true,
  });
}

export async function logout(): Promise<void> {
  try {
    await apiFetch(`${AUTH_BASE}/logout`, { method: 'POST', json: {}, anonymous: true });
  } finally {
    setAccessToken(null);
  }
}

export function getMe(): Promise<UserProfile> {
  return apiFetch<UserProfile>('/api/identity/v1/users/me');
}

export function updateMe(displayName: string): Promise<UserProfile> {
  return apiFetch<UserProfile>('/api/identity/v1/users/me', { method: 'PATCH', json: { displayName } });
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await apiFetch('/api/identity/v1/users/me/password', {
    method: 'POST',
    json: { currentPassword, newPassword },
  });
  setAccessToken(null); // mọi phiên đã bị thu hồi — đăng nhập lại
}
