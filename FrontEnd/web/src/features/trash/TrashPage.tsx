import DeleteForeverOutlinedIcon from '@mui/icons-material/DeleteForeverOutlined';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { emptyTrash, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { folderPath } from '../drive/download';
import { BarButton, ItemMenu, SelectionBar, type MenuState } from '../drive/ItemMenu';
import { ItemTable } from '../drive/ItemViews';
import { refreshDrive, useTrash } from '../drive/queries';
import { useSelection } from '../drive/selection';
import { useItemActions } from '../drive/useItemActions';

const trashedColumn = { label: 'Ngày xoá', value: (item: DriveItem) => item.trashedAt };

/**
 * Thùng rác: các mục người dùng đã xoá (thư mục hiện như một mục, kèm mọi thứ bên trong). Khôi phục về chỗ cũ, hoặc
 * về "Drive của tôi" nếu thư mục chứa nó cũng đang ở thùng rác. Tự xoá vĩnh viễn sau 30 ngày.
 */
export default function TrashPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const trash = useTrash();
  const actions = useItemActions();
  const items = trash.data?.pages.flatMap((p) => p.items) ?? [];
  const selection = useSelection(items);
  const [menu, setMenu] = useState<MenuState | null>(null);
  const [confirmEmpty, setConfirmEmpty] = useState(false);

  const empty = useMutation({
    mutationFn: emptyTrash,
    onSettled: () => refreshDrive(queryClient),
    onSuccess: () => setConfirmEmpty(false),
  });

  function open(item: DriveItem) {
    // Thư mục trong thùng rác mở ở chế độ chỉ xem; file thì xem thông tin (khôi phục trước khi dùng).
    if (item.kind === 'folder') navigate(folderPath(item));
    else actions.details(item);
  }

  const { selectedItems } = selection;

  return (
    <Box>
      <Stack direction="row" sx={{ alignItems: 'center', mb: 2, minHeight: 40 }}>
        <Typography variant="h6" component="h1" sx={{ flexGrow: 1 }}>
          Thùng rác
        </Typography>
        <Button
          color="error"
          startIcon={<DeleteForeverOutlinedIcon />}
          disabled={items.length === 0}
          onClick={() => setConfirmEmpty(true)}
        >
          Dọn sạch thùng rác
        </Button>
      </Stack>

      <Alert severity="info" icon={<DeleteOutlineIcon />} sx={{ mb: 2 }}>
        Các mục trong thùng rác sẽ bị xoá vĩnh viễn sau 30 ngày và vẫn được tính vào dung lượng cho tới lúc
        đó.
      </Alert>

      <SelectionBar
        count={selectedItems.length}
        onClear={selection.clear}
        hint="Nhấp chuột phải vào một mục để khôi phục hoặc xoá vĩnh viễn"
      >
        <BarButton
          title="Khôi phục"
          onClick={() => {
            actions.restore(selectedItems);
            selection.clear();
          }}
        >
          <RestoreFromTrashIcon fontSize="small" />
        </BarButton>
        <BarButton title="Xoá vĩnh viễn" onClick={() => actions.purge(selectedItems)}>
          <DeleteForeverOutlinedIcon fontSize="small" />
        </BarButton>
        {selectedItems.length === 1 && (
          <BarButton title="Thông tin chi tiết" onClick={() => actions.details(selectedItems[0]!)}>
            <InfoOutlinedIcon fontSize="small" />
          </BarButton>
        )}
      </SelectionBar>

      {trash.isError && <Alert severity="error">{toUserMessage(trash.error)}</Alert>}
      {trash.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}

      {trash.isSuccess && items.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <DeleteOutlineIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Thùng rác trống
          </Typography>
          <Typography>Các mục bạn xoá sẽ nằm ở đây 30 ngày trước khi bị xoá vĩnh viễn.</Typography>
        </Stack>
      )}

      {items.length > 0 && (
        <ItemTable
          items={items}
          selected={selection.selected}
          onToggle={selection.toggle}
          onToggleAll={selection.toggleAll}
          onOpen={open}
          onMenu={(item, anchor) => setMenu({ item, anchor })}
          dateColumn={trashedColumn}
        />
      )}

      {trash.hasNextPage && (
        <Box sx={{ textAlign: 'center', mt: 2 }}>
          <Button onClick={() => void trash.fetchNextPage()} disabled={trash.isFetchingNextPage}>
            Tải thêm
          </Button>
        </Box>
      )}

      <ItemMenu
        state={menu}
        targets={menu ? selection.targetsFor(menu.item) : []}
        actions={actions}
        mode="trash"
        onClose={() => setMenu(null)}
      />
      {actions.elements}

      <Dialog open={confirmEmpty} onClose={() => setConfirmEmpty(false)} maxWidth="xs">
        <DialogTitle>Dọn sạch thùng rác?</DialogTitle>
        <DialogContent>
          {empty.isError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              {toUserMessage(empty.error)}
            </Alert>
          )}
          <DialogContentText>
            Mọi mục trong thùng rác sẽ bị xoá vĩnh viễn và bạn không thể khôi phục chúng.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmEmpty(false)}>Huỷ</Button>
          <Button color="error" variant="contained" disabled={empty.isPending} onClick={() => empty.mutate()}>
            {empty.isPending ? 'Đang xoá…' : 'Xoá vĩnh viễn'}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
