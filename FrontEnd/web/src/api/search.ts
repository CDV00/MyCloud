import type { ItemKind } from './drive';
import { apiFetch } from './http';

// Search service (docs/03 §6): view đọc chấp nhận trễ vài giây, xây từ event của Drive.

export type Category =
  'folder' | 'image' | 'video' | 'audio' | 'pdf' | 'doc' | 'sheet' | 'slide' | 'archive' | 'other';

export interface SearchHit {
  id: string;
  parentId: string | null;
  kind: ItemKind;
  name: string;
  mimeType: string | null;
  category: Category;
  sizeBytes: number;
  modifiedAt: string;
  ownerId: string;
}

export interface SharedHit {
  item: SearchHit;
  role: 'viewer' | 'commenter' | 'editor';
  sharedBy: string;
  sharedAt: string;
}

export interface SearchPage {
  items: SearchHit[];
  nextPageToken: string | null;
}

export interface SearchFilters {
  q: string;
  type?: Category | null;
  /** ISO 8601 — chỉ item sửa từ thời điểm này. */
  modifiedAfter?: string | null;
  owner?: 'me' | 'others' | null;
}

export interface RecentHit {
  item: SearchHit;
  lastAccessAt: string;
  action: 'open' | 'download' | 'upload' | 'edit';
}

export type ActivityAction = 'created' | 'renamed' | 'moved' | 'trashed' | 'restored' | 'version_added';

export interface ActivityEntry {
  id: string;
  itemId: string;
  kind: ItemKind | '';
  name: string;
  actorId: string;
  action: ActivityAction;
  at: string;
  previousName: string | null;
  fromFolder: string | null;
  toFolder: string | null;
  versionNo: number | null;
}

const SEARCH = '/api/search/v1';

export function search(filters: SearchFilters, pageToken?: string | null): Promise<SearchPage> {
  const query = new URLSearchParams({ q: filters.q });
  if (filters.type) query.set('type', filters.type);
  if (filters.modifiedAfter) query.set('modifiedAfter', filters.modifiedAfter);
  if (filters.owner) query.set('owner', filters.owner);
  if (pageToken) query.set('pageToken', pageToken);
  return apiFetch<SearchPage>(`${SEARCH}/search?${query}`);
}

export const getRecent = () => apiFetch<RecentHit[]>(`${SEARCH}/recent`);

export const getActivity = (itemId: string) =>
  apiFetch<ActivityEntry[]>(`${SEARCH}/items/${itemId}/activity`);

/** Item được chia sẻ trực tiếp cho tôi, mới chia sẻ trước. */
export const getSharedWithMe = () => apiFetch<SharedHit[]>(`${SEARCH}/shared-with-me`);
