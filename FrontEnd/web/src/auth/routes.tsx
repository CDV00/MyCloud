import { Box, CircularProgress } from '@mui/material';
import { Navigate, Outlet, useLocation } from 'react-router';
import { useAuth } from './authContext';

function FullPageSpinner() {
  return (
    <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
      <CircularProgress aria-label="Đang tải" />
    </Box>
  );
}

/** Chỉ cho vào khi đã đăng nhập; nếu chưa, chuyển tới /login và nhớ trang đang mở. */
export function ProtectedRoute() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') return <FullPageSpinner />;
  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  return <Outlet />;
}

/** Trang đăng nhập/đăng ký: đã đăng nhập thì quay lại trang định mở (mặc định trang chủ). */
export function PublicOnlyRoute() {
  const { status } = useAuth();
  const location = useLocation();

  if (status === 'loading') return <FullPageSpinner />;
  if (status === 'authenticated') {
    const from = (location.state as { from?: string } | null)?.from;
    return <Navigate to={from?.startsWith('/') ? from : '/'} replace />;
  }
  return <Outlet />;
}
