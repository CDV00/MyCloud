import { createContext, useContext } from 'react';
import type { UserProfile } from '../api/auth';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthState {
  status: AuthStatus;
  user: UserProfile | null;
}

export interface AuthContextValue extends AuthState {
  login(email: string, password: string): Promise<void>;
  register(email: string, password: string, displayName: string): Promise<void>;
  logout(): Promise<void>;
  /** Tải lại hồ sơ sau khi đổi tên hiển thị. */
  reloadProfile(): Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used inside <AuthProvider>.');
  }
  return context;
}
