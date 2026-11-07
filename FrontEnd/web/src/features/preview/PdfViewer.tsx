import { Alert, Box, CircularProgress, Typography } from '@mui/material';
import { getDocument, GlobalWorkerOptions, type PDFDocumentProxy } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { useEffect, useRef, useState } from 'react';

GlobalWorkerOptions.workerSrc = workerUrl;

const MAX_PAGES = 300;
const PAGE_WIDTH = 880;

/**
 * Xem PDF bằng PDF.js (tải riêng khi cần — module này nằm trong chunk lazy). Mỗi trang chỉ được vẽ khi cuộn tới gần
 * (IntersectionObserver) ⇒ tài liệu nhiều trang mở nhanh. PDF.js không chạy script nhúng trong PDF; tắt XFA.
 */
export default function PdfViewer({ url }: { url: string }) {
  // Kết quả gắn với URL đã tải ⇒ đổi file thì kết quả cũ tự bị bỏ qua (không cần đặt lại state trong effect).
  const [loaded, setLoaded] = useState<{ url: string; doc: PDFDocumentProxy | null; error: boolean } | null>(
    null,
  );

  useEffect(() => {
    const task = getDocument({ url, enableXfa: false });
    task.promise.then(
      (doc) => setLoaded({ url, doc, error: false }),
      () => setLoaded({ url, doc: null, error: true }),
    );
    return () => {
      void task.destroy();
    };
  }, [url]);

  const current = loaded?.url === url ? loaded : null;
  const doc = current?.doc ?? null;
  const error = current?.error ?? false;

  if (error) return <Alert severity="error">Không mở được tệp PDF này.</Alert>;
  if (!doc) return <CircularProgress sx={{ color: 'grey.300' }} />;

  const pages = Math.min(doc.numPages, MAX_PAGES);
  return (
    <Box sx={{ display: 'grid', gap: 2, justifyItems: 'center', width: '100%' }}>
      {Array.from({ length: pages }, (_, i) => (
        <PdfPage key={i} doc={doc} pageNumber={i + 1} />
      ))}
      {doc.numPages > pages && (
        <Typography color="grey.300">
          Chỉ hiển thị {pages} trang đầu — tải xuống để xem toàn bộ {doc.numPages} trang.
        </Typography>
      )}
    </Box>
  );
}

function PdfPage({ doc, pageNumber }: { doc: PDFDocumentProxy; pageNumber: number }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [visible, setVisible] = useState(pageNumber <= 2);
  const [height, setHeight] = useState(PAGE_WIDTH * 1.414);

  useEffect(() => {
    const element = canvas.current;
    if (!element || visible) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) setVisible(true);
      },
      { rootMargin: '600px' },
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, [visible]);

  useEffect(() => {
    if (!visible || !canvas.current) return;
    let cancelled = false;
    let renderTask: { cancel(): void } | null = null;
    void doc.getPage(pageNumber).then((page) => {
      if (cancelled || !canvas.current) return;
      const width = Math.min(PAGE_WIDTH, window.innerWidth - 32);
      const base = page.getViewport({ scale: 1 });
      const ratio = window.devicePixelRatio || 1;
      const viewport = page.getViewport({ scale: (width / base.width) * ratio });
      const target = canvas.current;
      target.width = Math.floor(viewport.width);
      target.height = Math.floor(viewport.height);
      target.style.width = `${width}px`;
      target.style.height = `${viewport.height / ratio}px`;
      setHeight(viewport.height / ratio);
      const task = page.render({ canvas: target, viewport });
      renderTask = task;
      task.promise.catch(() => undefined); // huỷ khi đóng / đổi trang
    });
    return () => {
      cancelled = true;
      renderTask?.cancel();
    };
  }, [doc, pageNumber, visible]);

  return (
    <canvas
      ref={canvas}
      aria-label={`Trang ${pageNumber}`}
      style={{
        background: 'white',
        boxShadow: '0 1px 4px rgba(0,0,0,.4)',
        width: Math.min(PAGE_WIDTH, window.innerWidth - 32),
        height,
        maxWidth: '100%',
      }}
    />
  );
}
