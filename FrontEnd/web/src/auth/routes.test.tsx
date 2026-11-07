import { render, screen } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext';
import { ProtectedRoute, PublicOnlyRoute } from './routes';

function renderAt(path: string, status: AuthStatus) {
  const value: AuthContextValue = {
    status,
    user:
      status === 'authenticated'
        ? { id: '1', email: 'a@b.c', displayName: 'A', avatarUrl: null, createdAt: '', isAdmin: false }
        : null,
    login: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
    reloadProfile: vi.fn(),
  };
  const router = createMemoryRouter(
    [
      { element: <PublicOnlyRoute />, children: [{ path: '/login', element: <p>trang đăng nhập</p> }] },
      { element: <ProtectedRoute />, children: [{ path: '/my-drive', element: <p>drive của tôi</p> }] },
      { path: '/', element: <p>trang chủ</p> },
    ],
    { initialEntries: [path] },
  );
  render(
    <AuthContext.Provider value={value}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
  return router;
}

describe('ProtectedRoute', () => {
  it('chưa đăng nhập ⇒ chuyển tới /login và nhớ trang định mở', () => {
    const router = renderAt('/my-drive', 'anonymous');

    expect(screen.getByText('trang đăng nhập')).toBeInTheDocument();
    expect(router.state.location.state).toEqual({ from: '/my-drive' });
  });

  it('đã đăng nhập ⇒ hiển thị trang', () => {
    renderAt('/my-drive', 'authenticated');

    expect(screen.getByText('drive của tôi')).toBeInTheDocument();
  });

  it('đang khôi phục phiên ⇒ hiển thị spinner, chưa chuyển trang', () => {
    renderAt('/my-drive', 'loading');

    expect(screen.getByLabelText('Đang tải')).toBeInTheDocument();
  });
});

describe('PublicOnlyRoute', () => {
  it('đã đăng nhập mà mở /login ⇒ về trang chủ', () => {
    renderAt('/login', 'authenticated');

    expect(screen.getByText('trang chủ')).toBeInTheDocument();
  });
});
