import { apiFetch } from './http';

// Phase 1: đọc từ Search (read model), ghi vào Drive. Phase 2 chuyển đọc sang API của Drive (ADR-003).

export interface FolderItem {
  id: string;
  name: string;
  description: string;
  parentId: string | null;
}

export interface FileItem {
  id: string;
  name: string;
  contentType: string;
  sizeBytes: number;
  description: string;
  folderId: string;
}

export function listFolders(parentId: string | null): Promise<FolderItem[]> {
  const query = parentId ? `?parentId=${encodeURIComponent(parentId)}` : '';
  return apiFetch<FolderItem[]>(`/api/search/v1/folders${query}`);
}

export function listFiles(folderId: string): Promise<FileItem[]> {
  return apiFetch<FileItem[]>(`/api/search/v1/files?folderId=${encodeURIComponent(folderId)}`);
}

export async function createFolder(name: string, parentId: string | null): Promise<string> {
  const created = await apiFetch<{ id: string }>('/api/drive/v1/folders', {
    method: 'POST',
    json: { name, parentId },
  });
  return created.id;
}

export function getFolder(folderId: string): Promise<FolderItem> {
  return apiFetch<FolderItem>(`/api/search/v1/folders/${encodeURIComponent(folderId)}`);
}
