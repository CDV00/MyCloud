import { describe, expect, it, vi } from 'vitest';
import type { Archive } from '../../api/storage';
import { ArchiveStore, type ArchiveApi } from './archiveStore';

function archive(status: Archive['status']): Archive {
  return {
    id: 'a1',
    name: 'Dự án.zip',
    status,
    fileCount: 3,
    totalBytes: 30,
    sizeBytes: status === 'ready' ? 20 : null,
    skippedCount: 0,
    createdAt: '2026-11-07T00:00:00Z',
    expiresAt: null,
    error: null,
  };
}

function fakeApi(statuses: Archive['status'][]) {
  const opened: string[] = [];
  const api: ArchiveApi = {
    create: vi.fn(() =>
      Promise.resolve({ archiveId: 'a1', name: 'Dự án.zip', fileCount: 3, totalBytes: 30 }),
    ),
    get: vi.fn(() => Promise.resolve(archive(statuses.shift() ?? 'ready'))),
    downloadUrl: vi.fn(() => Promise.resolve({ url: 'https://zip', expiresAt: '' })),
    open: (url) => opened.push(url),
    sleep: () => Promise.resolve(),
  };
  return { api, opened };
}

describe('ArchiveStore', () => {
  it('theo dõi tới khi nén xong rồi tự tải về', async () => {
    const { api, opened } = fakeApi(['pending', 'building', 'ready']);
    const store = new ArchiveStore(api);

    await store.request([{ id: 'f1', name: 'Dự án' }]);

    expect(api.create).toHaveBeenCalledWith(['f1']);
    expect(api.get).toHaveBeenCalledTimes(3);
    expect(opened).toEqual(['https://zip']);
    expect(store.getSnapshot()[0]).toMatchObject({ status: 'ready', name: 'Dự án.zip', archiveId: 'a1' });
  });

  it('nén thất bại ⇒ dừng theo dõi, không tải', async () => {
    const { api, opened } = fakeApi(['failed']);
    const store = new ArchiveStore(api);

    await store.request([
      { id: 'f1', name: 'x' },
      { id: 'f2', name: 'y' },
    ]);

    expect(opened).toEqual([]);
    expect(store.getSnapshot()[0]!.status).toBe('failed');
  });

  it('yêu cầu bị từ chối (vd quá lớn) ⇒ báo lỗi', async () => {
    const { api } = fakeApi([]);
    vi.mocked(api.create).mockRejectedValueOnce(new Error('too large'));
    const store = new ArchiveStore(api);

    await store.request([{ id: 'f1', name: 'x' }]);

    expect(store.getSnapshot()[0]!.status).toBe('error');
  });
});
