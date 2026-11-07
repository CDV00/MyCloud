import { useQuery } from '@tanstack/react-query';
import { getThumbnails, type DriveItem, type ThumbnailBatch } from '../../api/drive';

const BATCH = 200;
// URL thumbnail có hạn 1 giờ: làm mới trước khi hết hạn.
const STALE_MS = 45 * 60 * 1000;

export type ThumbnailFetcher = (itemIds: string[], size: number) => Promise<ThumbnailBatch>;

/**
 * URL thumbnail (id item → URL) cho các item đang hiển thị: một request cho mỗi 200 item. Khoá cache gồm số version
 * ⇒ tải lên phiên bản mới thì thumbnail được lấy lại. Item chưa có thumbnail không được hỏi.
 */
export function useThumbnails(
  items: Pick<DriveItem, 'id' | 'kind' | 'hasThumbnail' | 'versionNo'>[],
  options: { size?: number; fetcher?: ThumbnailFetcher; scope?: string } = {},
): ReadonlyMap<string, string> {
  const size = options.size ?? 256;
  const wanted = items.filter((i) => i.kind === 'file' && i.hasThumbnail);
  const key = wanted.map((i) => `${i.id}:${i.versionNo}`).sort();
  const fetcher = options.fetcher ?? getThumbnails;

  const query = useQuery({
    queryKey: ['thumbnails', options.scope ?? 'drive', size, key],
    enabled: wanted.length > 0,
    staleTime: STALE_MS,
    refetchInterval: STALE_MS,
    queryFn: async () => {
      const ids = wanted.map((i) => i.id);
      const batches: Promise<ThumbnailBatch>[] = [];
      for (let i = 0; i < ids.length; i += BATCH) batches.push(fetcher(ids.slice(i, i + BATCH), size));
      const urls = new Map<string, string>();
      for (const batch of await Promise.all(batches)) {
        for (const t of batch.items) urls.set(t.itemId, t.url);
      }
      return urls;
    },
  });

  return query.data ?? EMPTY;
}

const EMPTY: ReadonlyMap<string, string> = new Map();
