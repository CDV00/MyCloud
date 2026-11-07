import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import * as authApi from '../api/auth';
import { onSessionChange, refreshAccessToken } from '../api/http';
import { AuthContext, type AuthState } from './authContext';

/**
 * Trạng thái đăng nhập THẬT: khi mở app, thử đổi cookie refresh token lấy access token
 * (thay cho bản cũ chỉ kiểm tra localStorage có chuỗi "token").
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<AuthState>({ status: 'loading', user: null });

  const loadProfile = useCallback(async () => {
    const user = await authApi.getMe();
    setState({ status: 'authenticated', user });
  }, []);

  // Khôi phục phiên khi tải trang.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const token = await refreshAccessToken();
      if (cancelled) return;
      if (!token) {
        setState({ status: 'anonymous', user: null });
        return;
      }
      try {
        await loadProfile();
      } catch {
        if (!cancelled) setState({ status: 'anonymous', user: null });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [loadProfile]);

  // Phiên hết hạn ở bất kỳ đâu (refresh thất bại, đăng xuất, đổi mật khẩu) ⇒ về trạng thái chưa đăng nhập.
  useEffect(
    () =>
      onSessionChange((token) => {
        if (token === null) {
          setState({ status: 'anonymous', user: null });
          queryClient.clear(); // không để lộ dữ liệu của phiên trước
        }
      }),
    [queryClient],
  );

  const login = useCallback(
    async (email: string, password: string) => {
      await authApi.login(email, password);
      await loadProfile();
    },
    [loadProfile],
  );

  const register = useCallback(
    async (email: string, password: string, displayName: string) => {
      await authApi.register(email, password, displayName);
      await login(email, password);
    },
    [login],
  );

  const logout = useCallback(() => authApi.logout(), []);

  const value = useMemo(
    () => ({ ...state, login, register, logout, reloadProfile: loadProfile }),
    [state, login, register, logout, loadProfile],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
