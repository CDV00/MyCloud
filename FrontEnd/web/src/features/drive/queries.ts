import { useInfiniteQuery, useQuery, type QueryClient } from '@tanstack/react-query';
import { getAncestors, getItem, getRoot, listChildren, type SortField } from '../../api/drive';
import { getQuota } from '../../api/storage';

export const driveKeys = {
  all: ['drive'] as const,
  root: ['drive', 'root'] as const,
  item: (id: string) => ['drive', 'item', id] as const,
  ancestors: (id: string) => ['drive', 'ancestors', id] as const,
  childrenOf: (folderId: string) => ['drive', 'children', folderId] as const,
  children: (folderId: string, sort: SortField, desc: boolean) =>
    ['drive', 'children', folderId, sort, desc] as const,
  quota: ['storage', 'quota'] as const,
};

export const useRoot = () => useQuery({ queryKey: driveKeys.root, queryFn: getRoot, staleTime: Infinity });

export const useItem = (id: string | undefined) =>
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

export const useQuota = () => useQuery({ queryKey: driveKeys.quota, queryFn: getQuota, staleTime: 10_000 });

/** Sau khi tạo / đổi tên / di chuyển / upload: làm mới các thư mục bị ảnh hưởng và dung lượng. */
export function invalidateFolders(queryClient: QueryClient, ...folderIds: (string | null | undefined)[]) {
  for (const id of new Set(folderIds)) {
    if (id) void queryClient.invalidateQueries({ queryKey: driveKeys.childrenOf(id) });
  }
}
