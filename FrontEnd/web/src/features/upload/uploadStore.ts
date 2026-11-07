import { abortUpload, completeUpload, startUpload, type DriveItem } from '../../api/drive';
import { signParts } from '../../api/storage';
import { putPartWithXhr, sleep, uploadFile, waitForOnline, type UploadDependencies } from './multipartUpload';

export type UploadStatus = 'queued' | 'uploading' | 'done' | 'error' | 'canceled';

export interface UploadTask {
  id: string;
  fileName: string;
  sizeBytes: number;
  /** Thư mục chứa file (để làm mới danh sách khi xong). */
  parentId: string;
  /** Có giá trị ⇒ đây là phiên bản mới của file này, không phải file mới. */
  targetItemId?: string;
  uploadedBytes: number;
  status: UploadStatus;
  error?: unknown;
  item?: DriveItem;
}

type Listener = () => void;
type DoneListener = (task: UploadTask) => void;

const browserDeps: UploadDependencies = {
  start: (target, file, signal) => startUpload(target, file, signal),
  signParts,
  complete: completeUpload,
  abort: async (uploadId) => {
    await abortUpload(uploadId);
  },
  putPart: putPartWithXhr,
  isOnline: () => navigator.onLine,
  waitForOnline,
  sleep,
};

const MAX_PARALLEL_FILES = 2;

/**
 * Hàng đợi upload toàn app (giữ khi chuyển trang). React đọc qua useSyncExternalStore.
 * Mỗi lần đổi trạng thái tạo mảng mới để React nhận ra thay đổi.
 */
export class UploadStore {
  // Id chỉ để phân biệt các dòng trong bảng tiến độ. KHÔNG dùng crypto.randomUUID: API này chỉ có trong
  // "secure context" (HTTPS hoặc localhost) — mở app qua http://<IP>:8081 thì nó không tồn tại.
  private nextId = 0;
  private tasks: UploadTask[] = [];
  private readonly files = new Map<string, File>();
  private readonly controllers = new Map<string, AbortController>();
  private readonly listeners = new Set<Listener>();
  private readonly doneListeners = new Set<DoneListener>();

  private readonly deps: UploadDependencies;

  constructor(deps: UploadDependencies) {
    this.deps = deps;
  }

  subscribe = (listener: Listener) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  /** Gọi khi một file upload xong (để làm mới danh sách thư mục, dung lượng). */
  onDone(listener: DoneListener) {
    this.doneListeners.add(listener);
    return () => {
      this.doneListeners.delete(listener);
    };
  }

  getSnapshot = () => this.tasks;

  add(files: Iterable<File>, parentId: string) {
    for (const file of files) {
      const id = `upload-${++this.nextId}`;
      this.files.set(id, file);
      this.tasks = [
        ...this.tasks,
        { id, fileName: file.name, sizeBytes: file.size, parentId, uploadedBytes: 0, status: 'queued' },
      ];
    }
    this.emit();
    this.pump();
  }

  /** Tải nội dung mới cho một file có sẵn — thành phiên bản kế tiếp, giữ nguyên tên và vị trí. */
  addVersion(file: File, item: Pick<DriveItem, 'id' | 'name' | 'parentId'>) {
    const id = `upload-${++this.nextId}`;
    this.files.set(id, file);
    this.tasks = [
      ...this.tasks,
      {
        id,
        fileName: item.name,
        sizeBytes: file.size,
        parentId: item.parentId ?? '',
        targetItemId: item.id,
        uploadedBytes: 0,
        status: 'queued',
      },
    ];
    this.emit();
    this.pump();
  }

  cancel(id: string) {
    const task = this.tasks.find((t) => t.id === id);
    if (!task || task.status === 'done' || task.status === 'canceled') return;
    this.controllers.get(id)?.abort(new DOMException('Canceled', 'AbortError'));
    this.update(id, { status: 'canceled' });
    this.pump();
  }

  retry(id: string) {
    const task = this.tasks.find((t) => t.id === id);
    if (!task || (task.status !== 'error' && task.status !== 'canceled') || !this.files.has(id)) return;
    this.update(id, { status: 'queued', uploadedBytes: 0, error: undefined });
    this.pump();
  }

  /** Bỏ các mục đã xong / đã huỷ khỏi bảng tiến độ. */
  clearFinished() {
    for (const t of this.tasks) {
      if (t.status === 'done' || t.status === 'canceled') this.files.delete(t.id);
    }
    this.tasks = this.tasks.filter((t) => t.status !== 'done' && t.status !== 'canceled');
    this.emit();
  }

  private pump() {
    const running = this.tasks.filter((t) => t.status === 'uploading').length;
    const queued = this.tasks
      .filter((t) => t.status === 'queued')
      .slice(0, Math.max(0, MAX_PARALLEL_FILES - running));
    for (const task of queued) void this.run(task);
  }

  private async run(task: UploadTask) {
    const file = this.files.get(task.id)!;
    const controller = new AbortController();
    this.controllers.set(task.id, controller);
    this.update(task.id, { status: 'uploading' });

    try {
      const target = task.targetItemId ? { targetItemId: task.targetItemId } : { parentId: task.parentId };
      const item = await uploadFile(file, target, this.deps, {
        signal: controller.signal,
        onProgress: (uploadedBytes) => this.update(task.id, { uploadedBytes }),
      });
      this.update(task.id, { status: 'done', uploadedBytes: file.size, item });
      this.files.delete(task.id);
      const done = this.tasks.find((t) => t.id === task.id)!;
      this.doneListeners.forEach((l) => l(done));
    } catch (error) {
      if (!controller.signal.aborted) this.update(task.id, { status: 'error', error });
    } finally {
      this.controllers.delete(task.id);
      this.pump();
    }
  }

  private update(id: string, patch: Partial<UploadTask>) {
    this.tasks = this.tasks.map((t) => (t.id === id ? { ...t, ...patch } : t));
    this.emit();
  }

  private emit() {
    this.listeners.forEach((l) => l());
  }
}

export const uploadStore = new UploadStore(browserDeps);
