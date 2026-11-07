import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { createFolder } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { driveKeys } from './queries';

interface Props {
  open: boolean;
  parentId: string | null;
  onClose(): void;
}

export function NewFolderDialog({ open, parentId, onClose }: Props) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState('Thư mục không có tiêu đề');

  const mutation = useMutation({
    mutationFn: () => createFolder(name.trim(), parentId),
    onSuccess: () => {
      // Danh sách đọc từ Search (cập nhật bất đồng bộ qua RabbitMQ) nên làm mới lại sau một nhịp.
      // Phase 2 đọc thẳng từ Drive ⇒ thấy ngay thư mục vừa tạo.
      const key = driveKeys.folders(parentId);
      void queryClient.invalidateQueries({ queryKey: key });
      setTimeout(() => void queryClient.invalidateQueries({ queryKey: key }), 1000);
      close();
    },
  });

  function close() {
    setName('Thư mục không có tiêu đề');
    mutation.reset();
    onClose();
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (name.trim()) mutation.mutate();
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
            onFocus={(e) => e.target.select()}
            slotProps={{ htmlInput: { maxLength: 255 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>Huỷ</Button>
          <Button type="submit" variant="contained" disabled={!name.trim() || mutation.isPending}>
            Tạo
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
