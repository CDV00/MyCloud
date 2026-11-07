import { useQuery } from '@tanstack/react-query';
import { getUsers, type UserSummary } from '../../api/users';
import { useAuth } from '../../auth/authContext';

/**
 * Tên hiển thị cho danh sách id (chủ sở hữu, người chia sẻ, người thực hiện). Một request cho cả danh sách, cache
 * theo tập id; id của chính mình hiển thị là "Tôi" / "Bạn" ở nơi gọi.
 */
export function useUsers(ids: (string | null | undefined)[]) {
  const unique = [...new Set(ids.filter((id): id is string => !!id))].sort();
  const query = useQuery({
    queryKey: ['users', unique],
    queryFn: () => getUsers(unique),
    enabled: unique.length > 0,
    staleTime: 5 * 60_000,
  });
  return new Map<string, UserSummary>((query.data ?? []).map((u) => [u.id, u]));
}

/** "Tôi" nếu là người đang đăng nhập, ngược lại tên hiển thị (hoặc "…" khi chưa tải xong). */
export function useDisplayName() {
  const { user } = useAuth();
  return (id: string | null | undefined, users: Map<string, UserSummary>, me = 'Tôi') =>
    !id ? '—' : id === user?.id ? me : (users.get(id)?.displayName ?? '…');
}
