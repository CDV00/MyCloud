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

export const signParts = (uploadId: string, partNumbers: number[]) =>
  apiFetch<PresignedPart[]>(`${STORAGE}/uploads/${uploadId}/part-urls`, {
    method: 'POST',
    json: { partNumbers },
  });
