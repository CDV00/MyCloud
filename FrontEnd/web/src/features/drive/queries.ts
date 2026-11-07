import { useInfiniteQuery, useQuery, type QueryClient } from '@tanstack/react-query';
import {
  getAncestors,
  getItem,
  getRoot,
  listChildren,
  listStarred,
  listTrash,
  listVersions,
  type SortField,
} from '../../api/drive';
import { getActivity, getRecent, search, type SearchFilters } from '../../api/search';
import { getQuota } from '../../api/storage';

export const driveKeys = {
  all: ['drive'] as const,
  root: ['drive', 'root'] as const,
  item: (id: string) => ['drive', 'item', id] as const,
  ancestors: (id: string) => ['drive', 'ancestors', id] as const,
  childrenOf: (folderId: string) => ['drive', 'children', folderId] as const,
  children: (folderId: string, sort: SortField, desc: boolean) =>
    ['drive', 'children', folderId, sort, desc] as const,
  trash: ['drive', 'trash'] as const,
  starred: ['drive', 'starred'] as const,
  versions: (id: string) => ['drive', 'versions', id] as const,
  quota: ['storage', 'quota'] as const,
};

export const searchKeys = {
  all: ['search'] as const,
  results: (filters: SearchFilters) => ['search', 'results', filters] as const,
  recent: ['search', 'recent'] as const,
  activity: (id: string) => ['search', 'activity', id] as const,
};

export const useRoot = () => useQuery({ queryKey: driveKeys.root, queryFn: getRoot, staleTime: Infinity });

export const useItem = (id: string | null | undefined) =>
  useQuery({ queryKey: driveKeys.item(id ?? ''), queryFn: () => getItem(id!), enabled: !!id });

export const useAncestors = (id: string | undefined) =>
  useQuery({ queryKey: driveKeys.ancestors(id ?? ''), queryFn: () => getAncestors(id!), enabled: !!id });

export const useChildren = (folderId: string | undefined, sort: SortField, desc: boolean) =>
  useInfiniteQuery({
    queryKey: driveKeys.children(folderId ?? '', sort, desc),
    queryFn: ({ pageParam }) => listChildren(folderId!, sort, desc, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextPageToken,
    enabled: !!folderId,
  });

export const useTrash = () =>
  useInfiniteQuery({
    queryKey: driveKeys.trash,
    queryFn: ({ pageParam }) => listTrash(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextPageToken,
  });

export const useStarred = () =>
  useInfiniteQuery({
    queryKey: driveKeys.starred,
    queryFn: ({ pageParam }) => listStarred(pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextPageToken,
  });

export const useVersions = (id: string | null | undefined, enabled = true) =>
  useQuery({
    queryKey: driveKeys.versions(id ?? ''),
    queryFn: () => listVersions(id!),
    enabled: !!id && enabled,
  });

export const useQuota = () => useQuery({ queryKey: driveKeys.quota, queryFn: getQuota, staleTime: 10_000 });

export const useSearch = (filters: SearchFilters, enabled: boolean) =>
  useInfiniteQuery({
    queryKey: searchKeys.results(filters),
    queryFn: ({ pageParam }) => search(filters, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.nextPageToken,
    enabled,
  });

export const useRecent = () => useQuery({ queryKey: searchKeys.recent, queryFn: getRecent });

export const useActivity = (id: string | null | undefined, enabled = true) =>
  useQuery({
    queryKey: searchKeys.activity(id ?? ''),
    queryFn: () => getActivity(id!),
    enabled: !!id && enabled,
  });

/** Sau khi tạo / đổi tên / di chuyển / upload: làm mới các thư mục bị ảnh hưởng và dung lượng. */
export function invalidateFolders(queryClient: QueryClient, ...folderIds: (string | null | undefined)[]) {
  for (const id of new Set(folderIds)) {
    if (id) void queryClient.invalidateQueries({ queryKey: driveKeys.childrenOf(id) });
  }
}

/**
 * Sau thao tác ảnh hưởng nhiều nơi (thùng rác, gắn sao, sao chép, version): làm mới mọi dữ liệu Drive đang hiển thị,
 * dung lượng, và các view của Search (Search cập nhật trễ vài giây nên làm mới thêm một lần sau đó).
 */
export function refreshDrive(queryClient: QueryClient) {
  void queryClient.invalidateQueries({ queryKey: driveKeys.all });
  void queryClient.invalidateQueries({ queryKey: driveKeys.quota });
  void queryClient.invalidateQueries({ queryKey: searchKeys.all });
  setTimeout(() => void queryClient.invalidateQueries({ queryKey: searchKeys.all }), 2_000);
}
