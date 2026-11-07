import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  Radio,
  RadioGroup,
  Snackbar,
} from '@mui/material';
import { useState, useSyncExternalStore } from 'react';
import { toUserMessage } from '../../api/errors';
import { uploadPlanner, type ConflictChoice } from './uploadPlanner';

/** Hỏi một lần cho cả lô khi tải lên trùng tên với mục đã có trong thư mục (đặt một lần trong layout). */
export function UploadConflictDialog() {
  const { pending, error } = useSyncExternalStore(uploadPlanner.subscribe, uploadPlanner.getSnapshot);
  const [choice, setChoice] = useState<ConflictChoice>('keep-both');
  const conflicts = pending?.conflicts ?? [];
  const names = conflicts
    .slice(0, 5)
    .map((c) => `“${c.name}”`)
    .join(', ');
  const more = conflicts.length > 5 ? ` và ${conflicts.length - 5} mục khác` : '';
  const hasFolders = conflicts.some((c) => c.kind === 'folder');
  const hasFiles = conflicts.some((c) => c.kind === 'file');
  const replaceLabel =
    hasFiles && hasFolders
      ? 'Thay thế tệp (thành phiên bản mới) và gộp vào thư mục đã có'
      : hasFolders
        ? 'Gộp vào thư mục đã có'
        : 'Thay thế (tệp hiện có thành phiên bản cũ)';

  const close = (value: ConflictChoice | null) => {
    void uploadPlanner.resolve(value);
    setChoice('keep-both');
  };

  return (
    <>
      <Dialog open={pending !== null} onClose={() => close(null)} maxWidth="sm" fullWidth>
        <DialogTitle>
          {conflicts.length === 1 ? 'Đã có mục cùng tên' : `${conflicts.length} mục đã có trong thư mục này`}
        </DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            {names}
            {more} đã tồn tại. Bạn muốn xử lý thế nào?
          </DialogContentText>
          <RadioGroup value={choice} onChange={(e) => setChoice(e.target.value as ConflictChoice)}>
            <FormControlLabel
              value="keep-both"
              control={<Radio />}
              label="Giữ cả hai (mục mới được đổi tên, vd “tên (1)”)"
            />
            <FormControlLabel value="replace" control={<Radio />} label={replaceLabel} />
            <FormControlLabel value="skip" control={<Radio />} label="Bỏ qua các mục trùng tên" />
          </RadioGroup>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => close(null)}>Huỷ tải lên</Button>
          <Button variant="contained" onClick={() => close(choice)}>
            Tải lên
          </Button>
        </DialogActions>
      </Dialog>
      <Snackbar open={error != null} autoHideDuration={6000} onClose={() => uploadPlanner.clearError()}>
        <Alert severity="error" onClose={() => uploadPlanner.clearError()}>
          Không tải lên được: {toUserMessage(error)}
        </Alert>
      </Snackbar>
    </>
  );
}
