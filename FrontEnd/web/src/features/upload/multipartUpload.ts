import type { DriveItem, PresignedPart, UploadSession } from '../../api/drive';

/**
 * Upload một file theo kiểu S3 multipart, PUT thẳng từng part lên object storage bằng URL đã ký (ADR-002).
 * - Chạy song song vài part; part lỗi được thử lại với độ trễ tăng dần.
 * - Mất mạng: chờ có mạng lại rồi tiếp tục từ part đang dở (không upload lại part đã xong).
 * - URL hết hạn (403): xin ký lại part đó.
 * Hàm thuần, phụ thuộc truyền vào ⇒ test được không cần mạng.
 */

export interface UploadDependencies {
  start(parentId: string, file: File, signal: AbortSignal): Promise<UploadSession>;
  signParts(uploadId: string, partNumbers: number[]): Promise<PresignedPart[]>;
  complete(uploadId: string, parts: { partNumber: number; eTag: string }[]): Promise<DriveItem>;
  abort(uploadId: string): Promise<void>;
  /** PUT một part, trả ETag. Lỗi HTTP ⇒ ném {@link PartUploadError}. */
  putPart(
    url: string,
    body: Blob,
    onProgress: (loadedBytes: number) => void,
    signal: AbortSignal,
  ): Promise<string>;
  isOnline(): boolean;
  waitForOnline(signal: AbortSignal): Promise<void>;
  sleep(ms: number, signal: AbortSignal): Promise<void>;
}

export interface UploadOptions {
  signal: AbortSignal;
  onProgress?: (uploadedBytes: number) => void;
  /** Gọi khi đã có id upload (để huỷ được). */
  onStarted?: (uploadId: string) => void;
  concurrency?: number;
  /** Số lần thử lại mỗi part khi lỗi (không tính thời gian chờ mất mạng). */
  maxRetries?: number;
}

export class PartUploadError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`Part upload failed with HTTP ${status}`);
    this.name = 'PartUploadError';
    this.status = status;
  }
}

const SIGN_BATCH = 50;

export async function uploadFile(
  file: File,
  parentId: string,
  deps: UploadDependencies,
  options: UploadOptions,
): Promise<DriveItem> {
  const { signal, onProgress, onStarted, concurrency = 4, maxRetries = 6 } = options;

  const session = await deps.start(parentId, file, signal);
  onStarted?.(session.uploadId);

  // Một part hỏng hẳn ⇒ dừng luôn các part đang chạy song song (không upload vô ích).
  const internal = new AbortController();
  const forwardAbort = () => internal.abort(signal.reason);
  signal.addEventListener('abort', forwardAbort, { once: true });
  const partSignal = internal.signal;

  const urls = new Map(session.parts.map((p) => [p.partNumber, p.url]));
  const loaded = new Map<number, number>();
  const etags = new Map<number, string>();
  const reportProgress = () => onProgress?.([...loaded.values()].reduce((sum, n) => sum + n, 0));

  const urlFor = async (partNumber: number, forceResign = false): Promise<string> => {
    const known = urls.get(partNumber);
    if (known && !forceResign) return known;
    // Ký theo lô các part tiếp theo còn thiếu URL.
    const batch = forceResign
      ? [partNumber]
      : Array.from(
          { length: Math.min(SIGN_BATCH, session.partCount - partNumber + 1) },
          (_, i) => partNumber + i,
        ).filter((n) => !urls.has(n));
    for (const part of await deps.signParts(session.uploadId, batch)) {
      urls.set(part.partNumber, part.url);
    }
    return urls.get(partNumber)!;
  };

  const uploadPart = async (partNumber: number) => {
    const start = (partNumber - 1) * session.partSize;
    const body = file.slice(start, Math.min(start + session.partSize, file.size));
    let failures = 0;
    let resign = false;

    for (;;) {
      partSignal.throwIfAborted();
      if (!deps.isOnline()) {
        await deps.waitForOnline(partSignal);
      }

      try {
        const url = await urlFor(partNumber, resign);
        const etag = await deps.putPart(
          url,
          body,
          (n) => {
            loaded.set(partNumber, n);
            reportProgress();
          },
          partSignal,
        );
        etags.set(partNumber, etag);
        loaded.set(partNumber, body.size);
        reportProgress();
        return;
      } catch (error) {
        if (partSignal.aborted) throw partSignal.reason;
        loaded.set(partNumber, 0);
        reportProgress();

        // Mất mạng giữa chừng: không tính là một lần thất bại, chờ mạng rồi làm tiếp.
        if (!deps.isOnline()) continue;

        resign = error instanceof PartUploadError && error.status === 403; // URL hết hạn
        failures++;
        if (failures > maxRetries) throw error;
        await deps.sleep(Math.min(30_000, 1000 * 2 ** (failures - 1)), partSignal);
      }
    }
  };

  try {
    let next = 1;
    const worker = async () => {
      while (next <= session.partCount) {
        await uploadPart(next++);
      }
    };
    await Promise.all(Array.from({ length: Math.min(concurrency, session.partCount) }, worker));

    const parts = [...etags.entries()]
      .sort(([a], [b]) => a - b)
      .map(([partNumber, eTag]) => ({ partNumber, eTag }));
    return await deps.complete(session.uploadId, parts);
  } catch (error) {
    internal.abort(error);
    // Huỷ hoặc thất bại hẳn: trả quota đã giữ chỗ (lỗi khi huỷ không quan trọng — Storage tự dọn khi hết hạn).
    await deps.abort(session.uploadId).catch(() => undefined);
    throw signal.aborted ? signal.reason : error;
  } finally {
    signal.removeEventListener('abort', forwardAbort);
  }
}

/** PUT bằng XMLHttpRequest để có tiến độ upload (fetch chưa hỗ trợ). */
export function putPartWithXhr(
  url: string,
  body: Blob,
  onProgress: (loadedBytes: number) => void,
  signal: AbortSignal,
): Promise<string> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', url);
    xhr.upload.onprogress = (e) => onProgress(e.loaded);
    xhr.onload = () => {
      const etag = xhr.getResponseHeader('ETag');
      if (xhr.status >= 200 && xhr.status < 300 && etag) resolve(etag);
      else reject(new PartUploadError(xhr.status));
    };
    xhr.onerror = () => reject(new PartUploadError(0));
    xhr.ontimeout = () => reject(new PartUploadError(0));
    const onAbort = () => xhr.abort();
    signal.addEventListener('abort', onAbort, { once: true });
    xhr.onabort = () => reject(signal.reason ?? new DOMException('Aborted', 'AbortError'));
    xhr.onloadend = () => signal.removeEventListener('abort', onAbort);
    xhr.send(body);
  });
}

export function waitForOnline(signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (navigator.onLine) return resolve();
    const done = () => {
      window.removeEventListener('online', done);
      signal.removeEventListener('abort', aborted);
      resolve();
    };
    const aborted = () => {
      window.removeEventListener('online', done);
      reject(signal.reason);
    };
    window.addEventListener('online', done);
    signal.addEventListener('abort', aborted, { once: true });
  });
}

export function sleep(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        reject(signal.reason);
      },
      { once: true },
    );
  });
}
