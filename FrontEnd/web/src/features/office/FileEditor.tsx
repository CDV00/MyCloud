import CloseIcon from '@mui/icons-material/Close';
import HistoryIcon from '@mui/icons-material/History';
import SaveOutlinedIcon from '@mui/icons-material/SaveOutlined';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { canEdit, getDownloadUrl, getItem, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { ApiError } from '../../api/http';
import { FileIcon } from '../drive/FileIcon';
import { driveKeys, refreshDrive } from '../drive/queries';
import { DOCX_MIME, editKind, XLSX_MIME, type EditKind } from '../preview/previewKind';
import { FileHistoryPanel, PresenceAvatars } from '../collab/FileHistoryPanel';
import { useItemRoom } from '../collab/useItemRoom';
import { uploadNow } from '../upload/uploadStore';
import { fetchBytes } from './content';
import type { EditorProps } from './editors/types';

const TextEditor = lazy(() => import('./editors/TextEditor'));
const SheetEditor = lazy(() => import('./editors/SheetEditor'));
const CsvEditor = lazy(() => import('./editors/SheetEditor').then((m) => ({ default: m.CsvEditor })));
const DocxEditor = lazy(() => import('./editors/DocxEditor'));

const EDITORS: Record<EditKind, React.ComponentType<EditorProps>> = {
  text: TextEditor,
  csv: CsvEditor,
  sheet: SheetEditor,
  docx: DocxEditor,
};

function mimeFor(kind: EditKind, item: DriveItem) {
  switch (kind) {
    case 'docx':
      return DOCX_MIME;
    case 'sheet':
      // Giữ MIME gốc (.xls ⇒ application/vnd.ms-excel); nội dung được ghi đúng định dạng đã đọc.
      return item.mimeType === 'application/vnd.ms-excel' ? item.mimeType : XLSX_MIME;
    case 'csv':
      return 'text/csv';
    default:
      return item.mimeType && item.mimeType !== 'application/octet-stream' ? item.mimeType : 'text/plain';
  }
}

/**
 * Trình sửa toàn màn hình (ADR-016). Mỗi lần lưu là một <b>phiên bản mới</b> của tệp (bản cũ còn trong lịch sử) và kèm
 * revision lúc mở: người khác đã lưu trước ⇒ hỏi ghi đè thay vì âm thầm làm mất thay đổi của họ.
 */
export default function FileEditor({ itemId, onClose }: { itemId: string; onClose(): void }) {
  const queryClient = useQueryClient();
  const item = useQuery({
    queryKey: ['editor-item', itemId],
    queryFn: () => getItem(itemId),
    staleTime: Infinity,
    gcTime: 0,
  });
  const content = useQuery({
    queryKey: ['editor-content', itemId, item.data?.revision],
    enabled: item.data !== undefined,
    staleTime: Infinity,
    gcTime: 0,
    queryFn: async () => fetchBytes((await getDownloadUrl(itemId, { inline: true })).url),
  });

  const serialize = useRef<(() => Promise<Blob>) | null>(null);
  const revision = useRef<number | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);
  const [confirmClose, setConfirmClose] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  // Hộp thoại "Lưu phiên bản mới" (ghi chú thay đổi); Ctrl+S lưu nhanh không cần ghi chú.
  const [noteOpen, setNoteOpen] = useState(false);
  const [note, setNote] = useState('');
  const people = useItemRoom(itemId, 'editing');
  const othersEditing = people.filter((p) => p.mode === 'editing');

  // Revision lúc mở; sau mỗi lần lưu là revision của phiên bản vừa lưu.
  useEffect(() => {
    if (item.data && revision.current === null) revision.current = item.data.revision;
  }, [item.data]);
  const kind = item.data ? editKind(item.data) : null;
  const editable = item.data ? canEdit(item.data) && kind !== null && item.data.trashedAt === null : false;

  const register = useCallback((fn: () => Promise<Blob>) => {
    serialize.current = fn;
  }, []);
  const markDirty = useCallback(() => setDirty(true), []);

  const save = useCallback(
    async (overwrite = false, changeNote?: string) => {
      if (!item.data || !kind || !serialize.current || saving) return;
      setSaving(true);
      setError(null);
      try {
        const blob = await serialize.current();
        const file = new File([blob], item.data.name, { type: mimeFor(kind, item.data) });
        const saved = await uploadNow(file, {
          targetItemId: item.data.id,
          expectedRevision: overwrite ? undefined : (revision.current ?? undefined),
          source: 'edit', // ghi vào lịch sử chỉnh sửa: "đã chỉnh sửa", không phải "tải lên"
          note: changeNote?.trim() || undefined,
        });
        revision.current = saved.revision;
        setDirty(false);
        setSavedAt(new Date());
        setNote('');
        refreshDrive(queryClient);
        void queryClient.invalidateQueries({ queryKey: driveKeys.versions(saved.id) });
      } catch (e) {
        if (e instanceof ApiError && e.status === 412) setConflict(true);
        else setError(toUserMessage(e));
      } finally {
        setSaving(false);
      }
    },
    [item.data, kind, saving, queryClient],
  );

  // Ctrl+S lưu; rời trang khi chưa lưu ⇒ trình duyệt hỏi lại.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        if (editable) void save();
      }
    };
    const onUnload = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    window.addEventListener('beforeunload', onUnload);
    return () => {
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('beforeunload', onUnload);
    };
  }, [dirty, editable, save]);

  const requestClose = () => (dirty ? setConfirmClose(true) : onClose());
  const Editor = kind ? EDITORS[kind] : null;
  const status = saving
    ? 'Đang lưu…'
    : dirty
      ? 'Có thay đổi chưa lưu'
      : savedAt
        ? `Đã lưu lúc ${savedAt.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })}`
        : 'Mọi thay đổi đã được lưu';

  return (
    <Dialog
      open
      fullScreen
      onClose={requestClose}
      aria-label={item.data ? `Sửa ${item.data.name}` : 'Trình sửa'}
    >
      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: 'center', px: 2, py: 1, borderBottom: 1, borderColor: 'divider', minHeight: 56 }}
      >
        <IconButton onClick={requestClose} aria-label="Đóng trình sửa">
          <CloseIcon />
        </IconButton>
        {item.data && <FileIcon item={item.data} />}
        <Box sx={{ minWidth: 0, flexGrow: 1 }}>
          <Typography noWrap title={item.data?.name}>
            {item.data?.name ?? 'Đang mở…'}
          </Typography>
          {editable && (
            <Typography
              variant="caption"
              color={dirty ? 'warning.main' : 'text.secondary'}
              aria-live="polite"
            >
              {status}
            </Typography>
          )}
        </Box>
        <PresenceAvatars people={people} />
        {item.data && (
          <IconButton
            color={showHistory ? 'primary' : 'default'}
            onClick={() => setShowHistory((v) => !v)}
            aria-label="Lịch sử & trao đổi"
            aria-pressed={showHistory}
            title="Lịch sử & trao đổi"
          >
            <HistoryIcon />
          </IconButton>
        )}
        {editable && (
          <Button
            variant="contained"
            startIcon={saving ? <CircularProgress size={16} color="inherit" /> : <SaveOutlinedIcon />}
            disabled={saving || !dirty}
            onClick={() => setNoteOpen(true)}
            title="Lưu kèm ghi chú thay đổi (Ctrl+S: lưu nhanh)"
          >
            Lưu
          </Button>
        )}
      </Stack>
      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ borderRadius: 0 }}>
          Không lưu được: {error}
        </Alert>
      )}
      {editable && othersEditing.length > 0 && (
        <Alert severity="warning" sx={{ borderRadius: 0 }}>
          {othersEditing.map((p) => p.name).join(', ')} cũng đang sửa tệp này. Người lưu sau sẽ được hỏi trước
          khi ghi đè — hãy trao đổi trong “Lịch sử & trao đổi”.
        </Alert>
      )}

      <Box sx={{ flexGrow: 1, minHeight: 0, display: 'flex' }}>
        <Box sx={{ flexGrow: 1, minWidth: 0, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
          {item.error || content.error ? (
            <Alert severity="error" sx={{ m: 2 }}>
              {toUserMessage(item.error ?? content.error)}
            </Alert>
          ) : !item.data || (editable && !content.data) ? (
            <CircularProgress sx={{ m: 'auto' }} />
          ) : !editable ? (
            <Alert severity="info" sx={{ m: 2 }}>
              {kind === null
                ? 'Loại tệp này không sửa được trong trình duyệt.'
                : 'Bạn chỉ có quyền xem tệp này (hoặc tệp đang ở thùng rác).'}
            </Alert>
          ) : (
            <Suspense fallback={<CircularProgress sx={{ m: 'auto' }} />}>
              {Editor && (
                <Editor bytes={content.data!} name={item.data.name} onDirty={markDirty} register={register} />
              )}
            </Suspense>
          )}
        </Box>

        {showHistory && item.data && (
          <Box
            sx={{
              width: { xs: '100%', sm: 400 },
              flexShrink: 0,
              borderLeft: 1,
              borderColor: 'divider',
              minHeight: 0,
            }}
          >
            <FileHistoryPanel item={item.data} mode="editing" onClose={() => setShowHistory(false)} />
          </Box>
        )}
      </Box>

      <Dialog open={noteOpen} onClose={() => setNoteOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Lưu phiên bản mới</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            Ghi chú giúp người khác biết bạn đã thay đổi gì và vì sao (không bắt buộc). Bản trước vẫn được giữ
            trong lịch sử.
          </DialogContentText>
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={2}
            maxRows={6}
            label="Ghi chú thay đổi"
            placeholder="Vd: Cập nhật số liệu quý 3 theo góp ý của chị Lan"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                setNoteOpen(false);
                void save(false, note);
              }
            }}
            slotProps={{ htmlInput: { maxLength: 1000 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setNoteOpen(false)}>Huỷ</Button>
          <Button
            variant="contained"
            onClick={() => {
              setNoteOpen(false);
              void save(false, note);
            }}
          >
            Lưu
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={conflict} onClose={() => setConflict(false)} maxWidth="xs">
        <DialogTitle>Tệp vừa được người khác lưu</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Có phiên bản mới hơn được lưu sau khi bạn mở tệp. Ghi đè sẽ tạo phiên bản mới từ nội dung của bạn;
            phiên bản của người kia vẫn còn trong “Lịch sử & trao đổi”.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConflict(false)}>Huỷ</Button>
          <Button
            variant="contained"
            onClick={() => {
              setConflict(false);
              void save(true, note);
            }}
          >
            Vẫn lưu
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={confirmClose} onClose={() => setConfirmClose(false)} maxWidth="xs">
        <DialogTitle>Đóng mà không lưu?</DialogTitle>
        <DialogContent>
          <DialogContentText>Các thay đổi chưa lưu sẽ bị mất.</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmClose(false)}>Tiếp tục sửa</Button>
          <Button color="error" onClick={onClose}>
            Bỏ thay đổi
          </Button>
        </DialogActions>
      </Dialog>
    </Dialog>
  );
}
