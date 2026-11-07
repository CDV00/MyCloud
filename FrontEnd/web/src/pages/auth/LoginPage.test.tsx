import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/http';
import { AuthContext, type AuthContextValue } from '../../auth/authContext';
import LoginPage from './LoginPage';

function renderLogin(login: AuthContextValue['login']) {
  const value: AuthContextValue = {
    status: 'anonymous',
    user: null,
    login,
    register: vi.fn(),
    logout: vi.fn(),
    reloadProfile: vi.fn(),
  };
  const router = createMemoryRouter(
    [
      { path: '/login', element: <LoginPage /> },
      { path: '/my-drive', element: <p>drive của tôi</p> },
    ],
    { initialEntries: [{ pathname: '/login', state: { from: '/my-drive' } }] },
  );
  render(
    <AuthContext.Provider value={value}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

describe('LoginPage', () => {
  it('sai mật khẩu ⇒ hiển thị thông báo tiếng Việt', async () => {
    const login = vi.fn().mockRejectedValue(new ApiError(401, { code: 'IDENTITY.INVALID_CREDENTIALS' }));
    renderLogin(login);

    await userEvent.type(screen.getByLabelText(/Email/), 'a@b.c');
    await userEvent.type(screen.getByLabelText(/Mật khẩu/), 'sai-mat-khau');
    await userEvent.click(screen.getByRole('button', { name: 'Đăng nhập' }));

    expect(await screen.findByText('Email hoặc mật khẩu không đúng.')).toBeInTheDocument();
    expect(login).toHaveBeenCalledWith('a@b.c', 'sai-mat-khau');
  });

  it('đăng nhập thành công ⇒ quay về trang định mở', async () => {
    renderLogin(vi.fn().mockResolvedValue(undefined));

    await userEvent.type(screen.getByLabelText(/Email/), 'a@b.c');
    await userEvent.type(screen.getByLabelText(/Mật khẩu/), 'dung-mat-khau');
    await userEvent.click(screen.getByRole('button', { name: 'Đăng nhập' }));

    expect(await screen.findByText('drive của tôi')).toBeInTheDocument();
  });
});
