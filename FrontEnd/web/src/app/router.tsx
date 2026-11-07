import { createBrowserRouter, type RouteObject } from 'react-router';
import { ProtectedRoute, PublicOnlyRoute } from '../auth/routes';
import DrivePage from '../features/drive/DrivePage';
import StarredPage from '../features/drive/StarredPage';
import RecentPage from '../features/search/RecentPage';
import SearchPage from '../features/search/SearchPage';
import TrashPage from '../features/trash/TrashPage';
import { AppLayout } from '../layout/AppLayout';
import AccountPage from '../pages/AccountPage';
import LoginPage from '../pages/auth/LoginPage';
import RegisterPage from '../pages/auth/RegisterPage';
import ComingSoonPage from '../pages/ComingSoonPage';
import ErrorPage from '../pages/ErrorPage';
import HomePage from '../pages/HomePage';
import StoragePage from '../pages/StoragePage';

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
              { path: '/my-drive', element: <DrivePage /> },
              { path: '/my-drive/folders/:folderId', element: <DrivePage /> },
              { path: '/shared', element: <ComingSoonPage title="Được chia sẻ với tôi" phase={4} /> },
              { path: '/recent', element: <RecentPage /> },
              { path: '/starred', element: <StarredPage /> },
              { path: '/trash', element: <TrashPage /> },
              { path: '/search', element: <SearchPage /> },
              { path: '/storage', element: <StoragePage /> },
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
