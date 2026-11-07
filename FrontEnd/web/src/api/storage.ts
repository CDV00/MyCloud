import type { PresignedPart } from './drive';
import { apiFetch } from './http';

// Storage service: xin thêm URL part, xem dung lượng. Byte của file đi thẳng tới object storage (ADR-002).

export interface Quota {
  limitBytes: number;
  usedBytes: number;
  reservedBytes: number;
  availableBytes: number;
}

const STORAGE = '/api/storage/v1';

export const getQuota = () => apiFetch<Quota>(`${STORAGE}/quota`);

export type ArchiveStatus = 'pending' | 'building' | 'ready' | 'failed' | 'expired';

export interface Archive {
  id: string;
  name: string;
  status: ArchiveStatus;
  fileCount: number;
  totalBytes: number;
  sizeBytes: number | null;
  /** File không còn khi nén (đã bị xoá vĩnh viễn) nên không có trong zip. */
  skippedCount: number;
  createdAt: string;
  expiresAt: string | null;
  error: string | null;
}

export const getArchive = (id: string) => apiFetch<Archive>(`${STORAGE}/archives/${id}`);

export const getArchiveDownloadUrl = (id: string) =>
  apiFetch<{ url: string; expiresAt: string }>(`${STORAGE}/archives/${id}/download-url`, { method: 'POST' });

export const signParts = (uploadId: string, partNumbers: number[]) =>
  apiFetch<PresignedPart[]>(`${STORAGE}/uploads/${uploadId}/part-urls`, {
    method: 'POST',
    json: { partNumbers },
  });
