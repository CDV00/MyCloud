import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DriveItem } from '../../api/drive';
import type { UploadDependencies } from './multipartUpload';
import { UploadStore, type UploadTask } from './uploadStore';

function fakeDeps(): UploadDependencies {
  return {
    start: vi.fn().mockResolvedValue({
      uploadId: 'u1',
      partSize: 8,
      partCount: 1,
      parts: [{ partNumber: 1, url: 'url-1' }],
      expiresAt: '',
    }),
    signParts: vi.fn(),
    complete: vi.fn().mockResolvedValue({ id: 'file-1' } as DriveItem),
    abort: vi.fn().mockResolvedValue(undefined),
    putPart: vi.fn().mockResolvedValue('"etag"'),
    isOnline: () => true,
    waitForOnline: vi.fn(),
    sleep: vi.fn(),
  };
}

describe('UploadStore', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('chạy được khi mở qua http://<IP> (không phải secure context ⇒ không có crypto.randomUUID)', async () => {
    // Trình duyệt chỉ cung cấp crypto.randomUUID trên HTTPS / localhost.
    vi.stubGlobal('crypto', { getRandomValues: crypto.getRandomValues.bind(crypto) });
    const store = new UploadStore(fakeDeps());
    const done: UploadTask[] = [];
    const allDone = new Promise<void>((resolve) =>
      store.onDone((task) => {
        done.push(task);
        if (done.length === 2) resolve();
      }),
    );

    store.add([new File(['a'], 'a.txt'), new File(['b'], 'b.txt')], 'folder-1');
    await allDone;

    const tasks = store.getSnapshot();
    expect(tasks.map((t) => [t.fileName, t.status])).toEqual([
      ['a.txt', 'done'],
      ['b.txt', 'done'],
    ]);
    expect(new Set(tasks.map((t) => t.id)).size).toBe(2);
  });
});
