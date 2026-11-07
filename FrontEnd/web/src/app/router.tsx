import { createBrowserRouter, type RouteObject } from 'react-router';
import { ProtectedRoute, PublicOnlyRoute } from '../auth/routes';
import MyDrivePage from '../features/drive/MyDrivePage';
import { AppLayout } from '../layout/AppLayout';
import AccountPage from '../pages/AccountPage';
import LoginPage from '../pages/auth/LoginPage';
import RegisterPage from '../pages/auth/RegisterPage';
import ComingSoonPage from '../pages/ComingSoonPage';
import ErrorPage from '../pages/ErrorPage';
import HomePage from '../pages/HomePage';

export const routes: RouteObject[] = [
  {
    errorElement: <ErrorPage />,
    children: [
      {
        element: <PublicOnlyRoute />,
        children: [
          { path: '/login', element: <LoginPage /> },
          { path: '/register', element: <RegisterPage /> },
        ],
      },
      {
        element: <ProtectedRoute />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { index: true, element: <HomePage /> },
              { path: '/my-drive', element: <MyDrivePage /> },
              { path: '/my-drive/folders/:folderId', element: <MyDrivePage /> },
              { path: '/shared', element: <ComingSoonPage title="Được chia sẻ với tôi" phase={4} /> },
              { path: '/recent', element: <ComingSoonPage title="Gần đây" phase={3} /> },
              { path: '/starred', element: <ComingSoonPage title="Có gắn dấu sao" phase={3} /> },
              { path: '/trash', element: <ComingSoonPage title="Thùng rác" phase={3} /> },
              { path: '/storage', element: <ComingSoonPage title="Bộ nhớ" phase={2} /> },
              { path: '/account', element: <AccountPage /> },
              { path: '*', element: <ErrorPage /> },
            ],
          },
        ],
      },
    ],
  },
];

export const router = createBrowserRouter(routes);
