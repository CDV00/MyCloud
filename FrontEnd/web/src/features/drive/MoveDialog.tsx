import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import FolderIcon from '@mui/icons-material/Folder';
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Link,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Typography,
} from '@mui/material';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { moveItem, type DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { driveKeys, invalidateFolders, useAncestors, useChildren, useItem, useRoot } from './queries';

interface Props {
  items: DriveItem[];
  onClose(): void;
}

/** Chọn thư mục đích bằng cách duyệt cây; không cho chọn chính item đang di chuyển hoặc con cháu của nó. */
export function MoveDialog({ items, onClose }: Props) {
  return items.length > 0 ? <MoveForm items={items} onClose={onClose} /> : null;
}

function MoveForm({ items, onClose }: Props) {
  const queryClient = useQueryClient();
  const root = useRoot();
  const [browsingId, setBrowsingId] = useState<string | undefined>(items[0]!.parentId ?? undefined);
  const folderId = browsingId ?? root.data?.id;
  const folder = useItem(folderId);
  const ancestors = useAncestors(folderId);
  const children = useChildren(folderId, 'name', false);

  const movingIds = new Set(items.map((i) => i.id));
  const folders = (children.data?.pages.flatMap((p) => p.items) ?? []).filter(
    (i) => i.kind === 'folder' && !movingIds.has(i.id),
  );
  const insideMovingItem = [...(ancestors.data ?? []), ...(folder.data ? [folder.data] : [])].some((a) =>
    movingIds.has(a.id),
  );
  const alreadyThere = items.every((i) => i.parentId === folderId);

  const mutation = useMutation({
    mutationFn: async () => {
      for (const item of items) {
        if (item.parentId !== folderId) await moveItem(item, folderId!);
      }
    },
    onSettled: () => {
      invalidateFolders(queryClient, folderId, ...items.map((i) => i.parentId));
      for (const item of items)
        void queryClient.invalidateQueries({ queryKey: driveKeys.ancestors(item.id) });
    },
    onSuccess: onClose,
  });

  const title = items.length === 1 ? `Di chuyển “${items[0]!.name}”` : `Di chuyển ${items.length} mục`;

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{title}</DialogTitle>
      <DialogContent dividers sx={{ minHeight: 320 }}>
        {mutation.isError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {toUserMessage(mutation.error)}
          </Alert>
        )}
        <Breadcrumbs separator={<ChevronRightIcon fontSize="small" />} sx={{ mb: 1 }}>
          {(ancestors.data ?? []).map((a) => (
            <Link
              key={a.id}
              component="button"
              underline="hover"
              color="inherit"
              onClick={() => setBrowsingId(a.id)}
            >
              {a.isRoot ? 'Drive của tôi' : a.name}
            </Link>
          ))}
          <Typography color="text.primary">
            {folder.data ? (folder.data.isRoot ? 'Drive của tôi' : folder.data.name) : '…'}
          </Typography>
        </Breadcrumbs>
        {children.isPending ? (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 4 }}>
            <CircularProgress size={28} />
          </Box>
        ) : folders.length === 0 ? (
          <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
            Không có thư mục con
          </Typography>
        ) : (
          <List dense>
            {folders.map((f) => (
              <ListItemButton key={f.id} onClick={() => setBrowsingId(f.id)}>
                <ListItemIcon>
                  <FolderIcon sx={{ color: '#5f6368' }} />
                </ListItemIcon>
                <ListItemText primary={f.name} />
                <ChevronRightIcon fontSize="small" color="action" />
              </ListItemButton>
            ))}
          </List>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Huỷ</Button>
        <Button
          variant="contained"
          onClick={() => mutation.mutate()}
          disabled={!folderId || insideMovingItem || alreadyThere || mutation.isPending}
        >
          Di chuyển tới đây
        </Button>
      </DialogActions>
    </Dialog>
  );
}
