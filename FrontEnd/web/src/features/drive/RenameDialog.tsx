import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type FormEvent } from 'react';
import { renameItem, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { driveKeys, invalidateFolders } from './queries';

interface Props {
  item: DriveItem | null;
  onClose(): void;
}

/** Đổi tên — chọn sẵn phần tên, không chọn phần mở rộng (giống Drive). */
export function RenameDialog({ item, onClose }: Props) {
  return item ? <RenameForm key={item.id} item={item} onClose={onClose} /> : null;
}

function RenameForm({ item, onClose }: { item: DriveItem; onClose(): void }) {
  const queryClient = useQueryClient();
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(item.name);

  const mutation = useMutation({
    mutationFn: () => renameItem(item, name.trim()),
    onSuccess: (updated) => {
      queryClient.setQueryData(driveKeys.item(updated.id), updated);
      invalidateFolders(queryClient, item.parentId);
      void queryClient.invalidateQueries({ queryKey: driveKeys.ancestors(item.id) });
      onClose();
    },
  });

  function selectStem() {
    const input = inputRef.current;
    if (!input) return;
    const dot = item.kind === 'file' ? item.name.lastIndexOf('.') : -1;
    input.focus();
    input.setSelectionRange(0, dot > 0 ? dot : item.name.length);
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim() || name.trim() === item.name) {
      onClose();
      return;
    }
    mutation.mutate();
  }

  return (
    <Dialog
      open
      onClose={onClose}
      fullWidth
      maxWidth="xs"
      slotProps={{ transition: { onEntered: selectStem } }}
    >
      <form onSubmit={handleSubmit}>
        <DialogTitle>Đổi tên</DialogTitle>
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
            label="Tên mới"
            value={name}
            onChange={(e) => setName(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 255 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Huỷ</Button>
          <Button type="submit" variant="contained" disabled={!name.trim() || mutation.isPending}>
            Đổi tên
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
