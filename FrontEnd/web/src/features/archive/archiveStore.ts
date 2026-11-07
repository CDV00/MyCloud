import { createArchive, type DriveItem } from '../../api/drive';
import { getArchive, getArchiveDownloadUrl, type ArchiveStatus } from '../../api/storage';
import { openDownload } from '../drive/download';

export interface ArchiveTask {
  /** Id của yêu cầu nén (Storage) — null khi đang gửi yêu cầu. */
  archiveId: string | null;
  key: string;
  name: string;
  fileCount: number;
  status: ArchiveStatus | 'requesting' | 'error';
  error?: unknown;
  skippedCount: number;
}

export interface ArchiveApi {
  create: typeof createArchive;
  get: typeof getArchive;
  downloadUrl: typeof getArchiveDownloadUrl;
  open(url: string): void;
  sleep(ms: number): Promise<void>;
}

const POLL_MS = 1500;
const GIVE_UP_MS = 2 * 60 * 60 * 1000;

/**
 * Tải nhiều item / thư mục dạng zip (ADR-015): gửi yêu cầu, theo dõi trạng thái tới khi Storage nén xong rồi tự tải
 * về. Thông báo realtime (chuông) cũng báo khi xong — store này giúp người dùng đang mở trang không phải bấm thêm.
 */
export class ArchiveStore {
  private tasks: ArchiveTask[] = [];
  private nextKey = 0;
  private readonly listeners = new Set<() => void>();
  private readonly api: ArchiveApi;

  constructor(api: ArchiveApi) {
    this.api = api;
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = () => this.tasks;

  async request(items: Pick<DriveItem, 'id' | 'name'>[]): Promise<void> {
    const key = `archive-${++this.nextKey}`;
    const name = items.length === 1 ? `${items[0]!.name}.zip` : `${items.length} mục.zip`;
    this.tasks = [
      ...this.tasks,
      { key, archiveId: null, name, fileCount: 0, status: 'requesting', skippedCount: 0 },
    ];
    this.emit();

    try {
      const created = await this.api.create(items.map((i) => i.id));
      this.update(key, {
        archiveId: created.archiveId,
        name: created.name,
        fileCount: created.fileCount,
        status: 'pending',
      });
      await this.poll(key, created.archiveId);
    } catch (error) {
      this.update(key, { status: 'error', error });
    }
  }

  /** Tải tệp zip đã sẵn sàng (vd bấm từ thông báo). */
  async download(archiveId: string): Promise<void> {
    const { url } = await this.api.downloadUrl(archiveId);
    this.api.open(url);
  }

  dismiss(key: string) {
    this.tasks = this.tasks.filter((t) => t.key !== key);
    this.emit();
  }

  private async poll(key: string, archiveId: string) {
    const started = Date.now();
    while (Date.now() - started < GIVE_UP_MS) {
      if (!this.tasks.some((t) => t.key === key)) return; // người dùng đã đóng
      await this.api.sleep(POLL_MS);
      const archive = await this.api.get(archiveId);
      this.update(key, { status: archive.status, skippedCount: archive.skippedCount, name: archive.name });
      if (archive.status === 'ready') {
        if (this.tasks.some((t) => t.key === key)) await this.download(archiveId);
        return;
      }

      if (archive.status === 'failed' || archive.status === 'expired') return;
    }
  }

  private update(key: string, patch: Partial<ArchiveTask>) {
    this.tasks = this.tasks.map((t) => (t.key === key ? { ...t, ...patch } : t));
    this.emit();
  }

  private emit() {
    this.listeners.forEach((l) => l());
  }
}

export const archiveStore = new ArchiveStore({
  create: createArchive,
  get: getArchive,
  downloadUrl: getArchiveDownloadUrl,
  open: openDownload,
  sleep: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
});
