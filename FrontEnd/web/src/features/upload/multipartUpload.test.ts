import { describe, expect, it, vi } from 'vitest';
import type { DriveItem, UploadSession } from '../../api/drive';
import { PartUploadError, uploadFile, type UploadDependencies } from './multipartUpload';

const PART = 4;

function fileOf(size: number) {
  return new File([new Uint8Array(size)], 'báo cáo.bin');
}

function session(partCount: number, signedParts = partCount): UploadSession {
  return {
    uploadId: 'u1',
    partSize: PART,
    partCount,
    parts: Array.from({ length: signedParts }, (_, i) => ({ partNumber: i + 1, url: `url-${i + 1}` })),
    expiresAt: '',
  };
}

function deps(
  overrides: Partial<UploadDependencies> = {},
  partCount = 3,
): UploadDependencies & { calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    start: vi.fn().mockResolvedValue(session(partCount)),
    signParts: vi.fn(async (_id: string, numbers: number[]) =>
      numbers.map((n) => ({ partNumber: n, url: `resigned-${n}` })),
    ),
    complete: vi.fn().mockResolvedValue({ id: 'file-1' } as DriveItem),
    abort: vi.fn().mockResolvedValue(undefined),
    putPart: vi.fn(async (url: string, body: Blob, onProgress: (n: number) => void) => {
      calls.push(url);
      onProgress(body.size);
      return `"etag-${url}"`;
    }),
    isOnline: () => true,
    waitForOnline: vi.fn().mockResolvedValue(undefined),
    sleep: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  };
}

describe('uploadFile', () => {
  it('upload mọi part rồi hoàn tất với ETag theo thứ tự part', async () => {
    const d = deps();
    const progress: number[] = [];

    const item = await uploadFile(fileOf(10), { parentId: 'folder-1' }, d, {
      signal: new AbortController().signal,
      onProgress: (n) => progress.push(n),
    });

    expect(item.id).toBe('file-1');
    expect(d.complete).toHaveBeenCalledWith('u1', [
      { partNumber: 1, eTag: '"etag-url-1"' },
      { partNumber: 2, eTag: '"etag-url-2"' },
      { partNumber: 3, eTag: '"etag-url-3"' },
    ]);
    expect(progress.at(-1)).toBe(10); // 4 + 4 + 2 byte
    expect(d.abort).not.toHaveBeenCalled();
  });

  it('xin ký thêm URL cho part chưa có URL', async () => {
    const d = deps({ start: vi.fn().mockResolvedValue(session(3, 1)) });

    await uploadFile(fileOf(10), { parentId: 'f' }, d, {
      signal: new AbortController().signal,
      concurrency: 1,
    });

    expect(d.signParts).toHaveBeenCalledWith('u1', [2, 3]);
    expect(d.calls).toEqual(['url-1', 'resigned-2', 'resigned-3']);
  });

  it('part lỗi tạm thời được thử lại, không upload lại part đã xong', async () => {
    let failures = 0;
    const d = deps({
      putPart: vi.fn(async (url: string) => {
        if (url === 'url-2' && failures++ < 2) throw new PartUploadError(500);
        return `"etag-${url}"`;
      }),
    });

    await uploadFile(fileOf(10), { parentId: 'f' }, d, {
      signal: new AbortController().signal,
      concurrency: 1,
    });

    expect(d.putPart).toHaveBeenCalledTimes(5); // part 1, part 2 ×3, part 3
    expect(d.sleep).toHaveBeenCalledTimes(2);
    expect(d.complete).toHaveBeenCalled();
  });

  it('URL hết hạn (403) ⇒ ký lại đúng part đó', async () => {
    const d = deps(
      {
        putPart: vi.fn(async (url: string) => {
          if (url === 'url-1') throw new PartUploadError(403);
          return `"etag-${url}"`;
        }),
      },
      1,
    );
    d.start = vi.fn().mockResolvedValue(session(1));

    await uploadFile(fileOf(3), { parentId: 'f' }, d, { signal: new AbortController().signal });

    expect(d.signParts).toHaveBeenCalledWith('u1', [1]);
    expect(d.complete).toHaveBeenCalledWith('u1', [{ partNumber: 1, eTag: '"etag-resigned-1"' }]);
  });

  it('mất mạng ⇒ chờ có mạng rồi làm tiếp, không tính là thất bại', async () => {
    let online = true;
    let attempts = 0;
    const d = deps(
      {
        isOnline: () => online,
        putPart: vi.fn(async (url: string) => {
          if (attempts++ === 0) {
            online = false; // rớt mạng giữa lúc đang gửi part đầu
            throw new PartUploadError(0);
          }
          return `"etag-${url}"`;
        }),
        waitForOnline: vi.fn(async () => {
          online = true;
        }),
      },
      1,
    );
    d.start = vi.fn().mockResolvedValue(session(1));

    await uploadFile(fileOf(3), { parentId: 'f' }, d, {
      signal: new AbortController().signal,
      maxRetries: 0,
    });

    expect(d.waitForOnline).toHaveBeenCalledTimes(1);
    expect(d.sleep).not.toHaveBeenCalled();
    expect(d.complete).toHaveBeenCalled();
  });

  it('lỗi mãi không hết ⇒ dừng, huỷ upload để trả quota, ném lỗi', async () => {
    const d = deps({ putPart: vi.fn().mockRejectedValue(new PartUploadError(500)) });

    await expect(
      uploadFile(fileOf(10), { parentId: 'f' }, d, { signal: new AbortController().signal, maxRetries: 2 }),
    ).rejects.toBeInstanceOf(PartUploadError);

    expect(d.abort).toHaveBeenCalledWith('u1');
    expect(d.complete).not.toHaveBeenCalled();
  });

  it('người dùng huỷ ⇒ ngừng upload và huỷ phía server', async () => {
    const controller = new AbortController();
    const d = deps({
      putPart: vi.fn(async (_url: string, _body: Blob, _p: (n: number) => void, signal: AbortSignal) => {
        controller.abort(new DOMException('Canceled', 'AbortError'));
        throw signal.reason;
      }),
    });

    await expect(
      uploadFile(fileOf(10), { parentId: 'f' }, d, { signal: controller.signal }),
    ).rejects.toMatchObject({
      name: 'AbortError',
    });
    expect(d.abort).toHaveBeenCalledWith('u1');
    expect(d.complete).not.toHaveBeenCalled();
  });
});
