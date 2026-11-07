import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import CloseIcon from '@mui/icons-material/Close';
import DownloadIcon from '@mui/icons-material/Download';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import HistoryIcon from '@mui/icons-material/History';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  IconButton,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { lazy, Suspense, useEffect, useState } from 'react';
import { canEdit, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { formatSize } from '../../lib/format';
import { FileIcon } from '../drive/FileIcon';
import { editKind, previewKind } from './previewKind';

const PdfViewer = lazy(() => import('./PdfViewer'));
const OfficeViewer = lazy(() => import('../office/OfficeViewer'));
// Tải lười: panel dùng lại FilePreview để xem từng phiên bản (tránh vòng import).
const FileHistoryPanel = lazy(() =>
  import('../collab/FileHistoryPanel').then((m) => ({ default: m.FileHistoryPanel })),
);

export interface FilePreviewProps {
  /** Các file có thể chuyển qua lại bằng ← / → (thường là các file trong thư mục đang xem). */
  items: DriveItem[];
  /** Vị trí file đang xem trong `items`; null = đóng. */
  index: number | null;
  onIndexChange(index: number | null): void;
  /** Presigned URL hiển thị trực tiếp (inline) của version hiện tại. */
  getUrl(item: DriveItem): Promise<string>;
  onDownload(item: DriveItem): void;
  /** Mở trình sửa (chỉ hiện với tệp sửa được và người có quyền sửa). */
  onEdit?(item: DriveItem): void;
  /** Có nút "Lịch sử & trao đổi" (tắt khi đang xem một phiên bản cũ). */
  history?: boolean;
  /** Mở sẵn ngăn lịch sử (vd bấm từ thông báo). */
  historyOpen?: boolean;
}

/**
 * Xem trước toàn màn hình (Phase 5): ảnh, video / âm thanh (phát trực tiếp từ object storage, tua được nhờ HTTP range),
 * PDF (PDF.js), văn bản / mã nguồn. Loại khác ⇒ nút tải xuống. Phím ← / → chuyển file, Esc đóng.
 */
export function FilePreview({
  items,
  index,
  onIndexChange,
  getUrl,
  onDownload,
  onEdit,
  history = false,
  historyOpen = false,
}: FilePreviewProps) {
  const [showHistory, setShowHistory] = useState(historyOpen);
  const item = index !== null ? items[index] : undefined;
  const kind = item ? previewKind(item) : 'none';
  const url = useQuery({
    queryKey: ['preview-url', item?.id, item?.versionNo],
    queryFn: () => getUrl(item!),
    enabled: item !== undefined && kind !== 'none',
    staleTime: 4 * 60 * 1000, // URL có hạn 5 phút
    gcTime: 4 * 60 * 1000,
  });

  const go = (delta: number) => {
    if (index === null || items.length === 0) return;
    const next = index + delta;
    if (next >= 0 && next < items.length) onIndexChange(next);
  };

  useEffect(() => {
    if (index === null) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft') go(-1);
      if (e.key === 'ArrowRight') go(1);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  return (
    <Dialog
      open={item !== undefined}
      onClose={() => onIndexChange(null)}
      fullScreen
      aria-label={item ? `Xem trước ${item.name}` : undefined}
      slotProps={{ paper: { sx: { bgcolor: '#202124', color: 'grey.100' } } }}
    >
      {item && (
        <>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 2, py: 1, minHeight: 56 }}>
            <IconButton color="inherit" onClick={() => onIndexChange(null)} aria-label="Đóng xem trước">
              <CloseIcon />
            </IconButton>
            <FileIcon item={item} />
            <Typography noWrap sx={{ flexGrow: 1 }} title={item.name}>
              {item.name}
            </Typography>
            <Typography variant="body2" sx={{ color: 'grey.400', display: { xs: 'none', sm: 'block' } }}>
              {formatSize(item.sizeBytes)}
            </Typography>
            {onEdit && canEdit(item) && editKind(item) && (
              <Button
                color="inherit"
                variant="outlined"
                size="small"
                startIcon={<EditOutlinedIcon />}
                onClick={() => onEdit(item)}
                sx={{ borderColor: 'grey.600' }}
              >
                Chỉnh sửa
              </Button>
            )}
            {history && (
              <Tooltip title="Lịch sử & trao đổi">
                <IconButton
                  color={showHistory ? 'primary' : 'inherit'}
                  onClick={() => setShowHistory((v) => !v)}
                  aria-label="Lịch sử & trao đổi"
                  aria-pressed={showHistory}
                >
                  <HistoryIcon />
                </IconButton>
              </Tooltip>
            )}
            <Tooltip title="Tải xuống">
              <IconButton color="inherit" onClick={() => onDownload(item)} aria-label="Tải xuống">
                <DownloadIcon />
              </IconButton>
            </Tooltip>
          </Stack>

          <Box sx={{ flexGrow: 1, minHeight: 0, display: 'flex' }}>
            <Box
              sx={{
                flexGrow: 1,
                minWidth: 0,
                overflow: 'auto',
                display: 'grid',
                placeItems: kind === 'pdf' || kind === 'text' || kind === 'docx' ? 'start center' : 'center',
                px: { xs: 1, sm: 8 },
                pb: 2,
                position: 'relative',
              }}
              onClick={(e) => {
                if (e.target === e.currentTarget) onIndexChange(null);
              }}
            >
              {kind === 'none' ? (
                <NoPreview item={item} onDownload={() => onDownload(item)} />
              ) : url.error ? (
                <Alert severity="error">{toUserMessage(url.error)}</Alert>
              ) : !url.data ? (
                <CircularProgress sx={{ color: 'grey.300' }} />
              ) : (
                <Content key={`${item.id}:${item.versionNo}`} item={item} kind={kind} url={url.data} />
              )}
            </Box>
            {history && showHistory && (
              <Box
                sx={{
                  width: { xs: '100%', sm: 400 },
                  flexShrink: 0,
                  borderLeft: 1,
                  borderColor: 'divider',
                  position: { xs: 'absolute', sm: 'static' },
                  inset: { xs: '56px 0 0 0', sm: 'auto' },
                  zIndex: 2,
                }}
              >
                <Suspense fallback={<CircularProgress sx={{ m: 3 }} />}>
                  <FileHistoryPanel key={item.id} item={item} onClose={() => setShowHistory(false)} />
                </Suspense>
              </Box>
            )}
          </Box>

          {index! > 0 && (
            <NavButton side="left" onClick={() => go(-1)} label="Tệp trước">
              <ChevronLeftIcon fontSize="large" />
            </NavButton>
          )}
          {index! < items.length - 1 && (
            <NavButton side="right" onClick={() => go(1)} label="Tệp sau">
              <ChevronRightIcon fontSize="large" />
            </NavButton>
          )}
        </>
      )}
    </Dialog>
  );
}

function Content({ item, kind, url }: { item: DriveItem; kind: string; url: string }) {
  const [failed, setFailed] = useState(false);
  if (failed) {
    return (
      <Alert severity="warning">Trình duyệt không phát được định dạng này — hãy tải xuống để xem.</Alert>
    );
  }

  switch (kind) {
    case 'image':
      return (
        <Box
          component="img"
          src={url}
          alt={item.name}
          onError={() => setFailed(true)}
          sx={{ maxWidth: '100%', maxHeight: 'calc(100vh - 88px)', objectFit: 'contain' }}
        />
      );
    case 'video':
      return (
        <Box
          component="video"
          src={url}
          controls
          autoPlay
          onError={() => setFailed(true)}
          sx={{ maxWidth: '100%', maxHeight: 'calc(100vh - 88px)', bgcolor: 'black' }}
        />
      );
    case 'audio':
      return (
        <Box
          component="audio"
          src={url}
          controls
          autoPlay
          onError={() => setFailed(true)}
          sx={{ width: 480, maxWidth: '100%' }}
        />
      );
    case 'docx':
    case 'sheet':
    case 'csv':
      return (
        <Suspense fallback={<CircularProgress sx={{ color: 'grey.300' }} />}>
          <OfficeViewer kind={kind} url={url} name={item.name} />
        </Suspense>
      );
    case 'pdf':
      return (
        <Suspense fallback={<CircularProgress sx={{ color: 'grey.300' }} />}>
          <PdfViewer url={url} />
        </Suspense>
      );
    default:
      return <TextPreview url={url} />;
  }
}

/** Văn bản / mã nguồn: Storage luôn trả `text/plain` khi xem trực tiếp nên không có gì được trình duyệt thực thi. */
function TextPreview({ url }: { url: string }) {
  const text = useQuery({
    queryKey: ['preview-text', url],
    queryFn: async () => {
      const response = await fetch(url);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.text();
    },
    staleTime: Infinity,
  });

  if (text.error) return <Alert severity="error">Không đọc được nội dung tệp.</Alert>;
  if (text.data === undefined) return <CircularProgress sx={{ color: 'grey.300' }} />;
  return (
    <Box
      component="pre"
      sx={{
        m: 0,
        p: 3,
        width: 'min(960px, 100%)',
        bgcolor: 'background.paper',
        color: 'text.primary',
        borderRadius: 2,
        fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace',
        fontSize: 13,
        lineHeight: 1.6,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
      }}
    >
      {text.data}
    </Box>
  );
}

function NoPreview({ item, onDownload }: { item: DriveItem; onDownload(): void }) {
  return (
    <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center' }}>
      <FileIcon item={item} sx={{ fontSize: 96 }} />
      <Typography variant="h6">Không có bản xem trước cho loại tệp này</Typography>
      <Button variant="contained" startIcon={<DownloadIcon />} onClick={onDownload}>
        Tải xuống
      </Button>
    </Stack>
  );
}

function NavButton({
  side,
  onClick,
  label,
  children,
}: {
  side: 'left' | 'right';
  onClick(): void;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <IconButton
      onClick={onClick}
      aria-label={label}
      sx={{
        position: 'fixed',
        top: '50%',
        [side]: 12,
        transform: 'translateY(-50%)',
        color: 'grey.100',
        bgcolor: 'rgba(0,0,0,.35)',
        '&:hover': { bgcolor: 'rgba(0,0,0,.55)' },
      }}
    >
      {children}
    </IconButton>
  );
}
