import type { DriveItem } from '../../api/drive';

type Listener = () => void;

/** Tệp đang mở trong trình sửa (một lúc một tệp) — mở được từ menu, xem trước, sau khi tạo tệp mới. */
class EditorStore {
  private item: Pick<DriveItem, 'id' | 'name'> | null = null;
  private readonly listeners = new Set<Listener>();

  subscribe = (listener: Listener) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = () => this.item;

  open(item: Pick<DriveItem, 'id' | 'name'>) {
    this.item = { id: item.id, name: item.name };
    this.emit();
  }

  close() {
    this.item = null;
    this.emit();
  }

  private emit() {
    this.listeners.forEach((l) => l());
  }
}

export const editorStore = new EditorStore();
