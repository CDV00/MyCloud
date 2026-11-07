import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { createFolder } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { invalidateFolders } from './queries';

interface Props {
  open: boolean;
  /** Thư mục chứa; null khi chưa biết (đang tải thư mục gốc). */
  parentId: string | null;
  onClose(): void;
}

const DEFAULT_NAME = 'Thư mục không có tiêu đề';

export function NewFolderDialog({ open, parentId, onClose }: Props) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(DEFAULT_NAME);

  const mutation = useMutation({
    mutationFn: () => createFolder(parentId!, name.trim()),
    onSuccess: () => {
      // Drive đọc nhất quán ngay (PostgreSQL) — làm mới là thấy thư mục mới.
      invalidateFolders(queryClient, parentId);
      close();
    },
  });

  function close() {
    setName(DEFAULT_NAME);
    mutation.reset();
    onClose();
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (name.trim() && parentId) mutation.mutate();
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      fullWidth
      maxWidth="xs"
      // Menu "Mới" vừa đóng trả focus về nút của nó, làm mất autoFocus ⇒ focus sau khi hộp thoại mở xong.
      slotProps={{ transition: { onEntered: () => inputRef.current?.select() } }}
    >
      <form onSubmit={handleSubmit}>
        <DialogTitle>Thư mục mới</DialogTitle>
        <DialogContent>
          {mutation.isError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {toUserMessage(mutation.error)}
            </Alert>
          )}
          <TextField
            inputRef={inputRef}
            fullWidth
            margin="dense"
            label="Tên thư mục"
            value={name}
            onChange={(e) => setName(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 255 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>Huỷ</Button>
          <Button
            type="submit"
            variant="contained"
            disabled={!name.trim() || !parentId || mutation.isPending}
          >
            Tạo
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
