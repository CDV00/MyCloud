import { CircularProgress } from '@mui/material';
import { lazy, Suspense, useSyncExternalStore } from 'react';
import { editorStore } from './editorStore';

// Trình sửa (TipTap, ExcelJS, docx…) chỉ tải khi mở lần đầu.
const FileEditor = lazy(() => import('./FileEditor'));

/** Đặt một lần trong layout: hiển thị trình sửa khi có tệp được mở bằng `editorStore.open`. */
export function EditorHost() {
  const item = useSyncExternalStore(editorStore.subscribe, editorStore.getSnapshot);
  if (!item) return null;
  return (
    <Suspense
      fallback={<CircularProgress sx={{ position: 'fixed', top: '50%', left: '50%', zIndex: 1400 }} />}
    >
      <FileEditor key={item.id} itemId={item.id} onClose={() => editorStore.close()} />
    </Suspense>
  );
}
