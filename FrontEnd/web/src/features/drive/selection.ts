import { useState } from 'react';
import type { DriveItem } from '../../api/drive';

/**
 * Chọn nhiều kiểu Drive: nhấp = chọn một, Ctrl/⌘ + nhấp hoặc checkbox = thêm/bớt. Tự bỏ chọn khi đổi
 * `scope` (vd mở thư mục khác). Id không còn trong danh sách bị bỏ qua.
 */
export function useSelection(items: DriveItem[], scope?: string) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [currentScope, setCurrentScope] = useState(scope);
  if (currentScope !== scope) {
    setCurrentScope(scope);
    setSelected(new Set());
  }

  const selectedItems = items.filter((i) => selected.has(i.id));

  function toggle(item: DriveItem, additive: boolean) {
    setSelected((prev) => {
      if (!additive) return prev.size === 1 && prev.has(item.id) ? new Set() : new Set([item.id]);
      const next = new Set(prev);
      if (next.has(item.id)) next.delete(item.id);
      else next.add(item.id);
      return next;
    });
  }

  function toggleAll() {
    setSelected((prev) => (prev.size === items.length ? new Set() : new Set(items.map((i) => i.id))));
  }

  /** Mục thao tác của menu chuột phải: cả vùng chọn nếu item nằm trong đó, ngược lại chỉ item. */
  function targetsFor(item: DriveItem): DriveItem[] {
    return selected.has(item.id) && selectedItems.length > 1 ? selectedItems : [item];
  }

  return { selected, selectedItems, toggle, toggleAll, targetsFor, clear: () => setSelected(new Set()) };
}
