import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  InputAdornment,
  TextField,
} from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { toUserMessage } from '../../api/errors';
import { refreshDrive } from '../drive/queries';
import { XLSX_MIME } from '../preview/previewKind';
import { NEW_FILE_KINDS, type NewFileKind } from './newFileKinds';
import { uploadNow } from '../upload/uploadStore';
import { editorStore } from './editorStore';

/** Nội dung ban đầu của tệp mới (thư viện Office chỉ tải khi cần). */
async function blankContent(kind: NewFileKind, title: string): Promise<Blob> {
  switch (kind) {
    case 'docx':
      return (await import('./docxExport')).emptyDocx();
    case 'xlsx': {
      const { Workbook } = await import('exceljs');
      const workbook = new Workbook();
      workbook.creator = 'MyCloud';
      workbook.addWorksheet('Trang tính 1');
      return new Blob([await workbook.xlsx.writeBuffer()], { type: XLSX_MIME });
    }
    case 'md':
      return new Blob([`# ${title}\n\n`], { type: 'text/markdown' });
    default:
      return new Blob(['\n'], { type: 'text/plain' });
  }
}

/** Tạo tệp mới trong thư mục đang xem rồi mở trình sửa. Trùng tên ⇒ Drive tự đặt "tên (1)". */
export function CreateFileDialog({
  kind,
  parentId,
  onClose,
}: {
  kind: NewFileKind | null;
  parentId: string | null;
  onClose(): void;
}) {
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const spec = kind ? NEW_FILE_KINDS[kind] : null;

  const close = () => {
    setName('');
    setError(null);
    onClose();
  };

  async function create() {
    if (!kind || !spec || !parentId) return;
    const title = (name.trim() || spec.defaultName).replace(/[\\/]/g, '-');
    setBusy(true);
    setError(null);
    try {
      const content = await blankContent(kind, title);
      const item = await uploadNow(new File([content], `${title}.${kind}`, { type: spec.mime }), {
        parentId,
      });
      refreshDrive(queryClient);
      close();
      editorStore.open(item);
    } catch (e) {
      setError(toUserMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={kind !== null} onClose={close} maxWidth="xs" fullWidth>
      <DialogTitle>{spec?.title}</DialogTitle>
      <DialogContent>
        <TextField
          autoFocus
          fullWidth
          margin="dense"
          label="Tên tệp"
          placeholder={spec?.defaultName}
          value={name}
          onChange={(e) => setName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !busy) void create();
          }}
          error={error !== null}
          helperText={error}
          slotProps={{
            htmlInput: { maxLength: 240 },
            input: { endAdornment: <InputAdornment position="end">.{kind}</InputAdornment> },
          }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>Huỷ</Button>
        <Button variant="contained" disabled={busy || !parentId} onClick={() => void create()}>
          {busy ? 'Đang tạo…' : 'Tạo'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
