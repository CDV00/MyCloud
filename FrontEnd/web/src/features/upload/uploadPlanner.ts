import {
  createFolderTree,
  findNameConflicts,
  type DriveItem,
  type FolderConflictMode,
  type NameConflict,
} from '../../api/drive';
import { uploadStore } from './uploadStore';

/** Một file cần tải lên, `path` tương đối so với thư mục đích ("Ảnh/2024/a.jpg"; file lẻ: "a.jpg"). */
export interface PickedFile {
  file: File;
  path: string;
}

/** Những gì người dùng chọn / thả: file (kèm đường dẫn) và thư mục (kể cả thư mục rỗng). */
export interface Picked {
  files: PickedFile[];
  folders: string[];
}

/** Trùng tên ở cấp cao nhất: giữ cả hai (đổi tên mục mới), thay thế (file ⇒ phiên bản mới, thư mục ⇒ gộp), bỏ qua. */
export type ConflictChoice = 'keep-both' | 'replace' | 'skip';

const dirOf = (path: string) => (path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '');
const topOf = (path: string) => path.split('/')[0]!;
const nameOf = (path: string) => path.slice(path.lastIndexOf('/') + 1);

/** Từ `<input type="file">` (có `webkitdirectory` thì mỗi file có `webkitRelativePath`). */
export function fromFileList(list: FileList | File[]): Picked {
  const files = Array.from(list).map((file) => ({ file, path: file.webkitRelativePath || file.name }));
  return { files, folders: foldersOf(files.map((f) => f.path)) };
}

/** Kéo thả: duyệt cây thư mục bằng File System Entry API (thư mục rỗng vẫn được giữ). */
export async function fromDataTransfer(data: DataTransfer): Promise<Picked> {
  const entries = Array.from(data.items)
    .map((item) => item.webkitGetAsEntry?.())
    .filter((e): e is FileSystemEntry => e != null);
  if (entries.length === 0) return fromFileList(data.files);

  const files: PickedFile[] = [];
  const folders: string[] = [];
  async function walk(entry: FileSystemEntry, path: string): Promise<void> {
    if (entry.isFile) {
      const file = await new Promise<File>((resolve, reject) =>
        (entry as FileSystemFileEntry).file(resolve, reject),
      );
      files.push({ file, path });
      return;
    }

    folders.push(path);
    const reader = (entry as FileSystemDirectoryEntry).createReader();
    // readEntries trả từng đợt (Chrome: tối đa 100) — đọc tới khi rỗng.
    for (;;) {
      const batch = await new Promise<FileSystemEntry[]>((resolve, reject) =>
        reader.readEntries(resolve, reject),
      );
      if (batch.length === 0) break;
      for (const child of batch) await walk(child, `${path}/${child.name}`);
    }
  }

  for (const entry of entries) await walk(entry, entry.name);
  return { files, folders: [...new Set([...folders, ...foldersOf(files.map((f) => f.path))])] };
}

/** Mọi thư mục (kể cả cha) chứa các file này. */
export function foldersOf(paths: string[]): string[] {
  const result = new Set<string>();
  for (const path of paths) {
    let dir = dirOf(path);
    while (dir) {
      result.add(dir);
      dir = dirOf(dir);
    }
  }

  return [...result];
}

/** Tên ở cấp cao nhất (file lẻ và thư mục gốc của mỗi cây) — chỉ những tên này có thể trùng với item đã có. */
export const topLevelNames = (picked: Picked) => [
  ...new Set([...picked.files.map((f) => topOf(f.path)), ...picked.folders.map(topOf)]),
];

export interface UploadApi {
  findConflicts: typeof findNameConflicts;
  createTree: typeof createFolderTree;
  add(files: File[], parentId: string): void;
  addVersion(file: File, target: Pick<DriveItem, 'id' | 'name' | 'parentId'>): void;
}

const browserApi: UploadApi = {
  findConflicts: findNameConflicts,
  createTree: createFolderTree,
  add: (files, parentId) => uploadStore.add(files, parentId),
  addVersion: (file, target) => uploadStore.addVersion(file, target),
};

/**
 * Thực hiện tải lên theo lựa chọn khi trùng tên: tạo cả cây thư mục trong một request (Drive), rồi đưa từng file vào
 * hàng đợi upload với thư mục cha đúng. "Thay thế": file trùng (mà người dùng sửa được) thành phiên bản mới, kể cả
 * file nằm trong thư mục được gộp.
 */
export async function executeUpload(
  folderId: string,
  picked: Picked,
  conflicts: NameConflict[],
  choice: ConflictChoice,
  api: UploadApi = browserApi,
): Promise<void> {
  const conflictByName = new Map(conflicts.map((c) => [c.name.normalize('NFC').toLowerCase(), c]));
  const conflictOf = (path: string) => conflictByName.get(topOf(path).normalize('NFC').toLowerCase());
  const keep = (path: string) => choice !== 'skip' || !conflictOf(path);
  const files = picked.files.filter((f) => keep(f.path));
  const folders = picked.folders.filter(keep);

  const mode: FolderConflictMode = choice === 'replace' ? 'merge' : 'rename';
  const tree =
    folders.length > 0 ? await api.createTree(folderId, folders, mode) : { folders: {}, merged: [] };

  // File sẽ thành phiên bản mới của file đã có (đường dẫn → file đích).
  const replacements = new Map<string, NameConflict>();
  if (choice === 'replace') {
    for (const f of files.filter((f) => !f.path.includes('/'))) {
      const existing = conflictOf(f.path);
      if (existing?.kind === 'file' && (existing.role === 'owner' || existing.role === 'editor')) {
        replacements.set(f.path, existing);
      }
    }

    // Thư mục được gộp có thể đã chứa file cùng tên.
    for (const merged of tree.merged) {
      const inside = files.filter((f) => dirOf(f.path) === merged);
      if (inside.length === 0) continue;
      const found = await api.findConflicts(
        tree.folders[merged]!,
        inside.map((f) => nameOf(f.path)),
      );
      const byName = new Map(found.map((c) => [c.name, c]));
      for (const f of inside) {
        const existing = byName.get(nameOf(f.path));
        if (existing?.kind === 'file' && (existing.role === 'owner' || existing.role === 'editor')) {
          replacements.set(f.path, existing);
        }
      }
    }
  }

  const byParent = new Map<string, File[]>();
  for (const f of files) {
    const dir = dirOf(f.path);
    const parentId = dir ? tree.folders[dir] : folderId;
    if (!parentId) continue;
    const target = replacements.get(f.path);
    if (target) {
      api.addVersion(f.file, { id: target.itemId, name: target.name, parentId });
    } else {
      byParent.set(parentId, [...(byParent.get(parentId) ?? []), f.file]);
    }
  }

  for (const [parentId, list] of byParent) api.add(list, parentId);
}

export interface PendingUpload {
  folderId: string;
  picked: Picked;
  conflicts: NameConflict[];
}

/** Trạng thái hiển thị: hộp thoại trùng tên đang chờ, lỗi gần nhất (vd không tạo được thư mục). */
export interface PlannerState {
  pending: PendingUpload | null;
  error: unknown;
}

/**
 * Điểm vào chung cho mọi cách tải lên (nút "Mới", kéo thả): hỏi Drive tên nào đã có; trùng ⇒ chờ người dùng chọn
 * trong hộp thoại (một lần cho cả lô), không trùng ⇒ tải luôn.
 */
export class UploadPlanner {
  private state: PlannerState = { pending: null, error: null };
  private readonly listeners = new Set<() => void>();
  private readonly api: UploadApi;

  constructor(api: UploadApi = browserApi) {
    this.api = api;
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = () => this.state;

  async upload(folderId: string, picked: Picked): Promise<void> {
    if (picked.files.length === 0 && picked.folders.length === 0) return;
    await this.guard(async () => {
      const conflicts = await this.api.findConflicts(folderId, topLevelNames(picked));
      if (conflicts.length === 0) {
        await executeUpload(folderId, picked, [], 'keep-both', this.api);
      } else {
        this.set({ pending: { folderId, picked, conflicts } });
      }
    });
  }

  /** Người dùng chọn cách xử lý trùng tên (null = huỷ cả lô). */
  async resolve(choice: ConflictChoice | null): Promise<void> {
    const pending = this.state.pending;
    this.set({ pending: null });
    if (pending && choice) {
      await this.guard(() =>
        executeUpload(pending.folderId, pending.picked, pending.conflicts, choice, this.api),
      );
    }
  }

  clearError() {
    this.set({ error: null });
  }

  private async guard(work: () => Promise<void>) {
    try {
      await work();
    } catch (error) {
      this.set({ error });
    }
  }

  private set(patch: Partial<PlannerState>) {
    this.state = { ...this.state, ...patch };
    this.listeners.forEach((l) => l());
  }
}

export const uploadPlanner = new UploadPlanner();
