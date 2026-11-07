import AccessTimeIcon from '@mui/icons-material/AccessTime';
import { Alert, Box, Skeleton, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import type { DriveItem } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { ItemMenu, type MenuState } from '../drive/ItemMenu';
import { ItemTable } from '../drive/ItemViews';
import { useRecent } from '../drive/queries';
import { useSelection } from '../drive/selection';
import { useItemActions } from '../drive/useItemActions';
import { hitToItem } from './hits';

/** "Gần đây": tệp bạn mở, tải xuống hoặc tải lên gần nhất (Search service, cập nhật trễ vài giây). */
export default function RecentPage() {
  const recent = useRecent();
  const actions = useItemActions();
  const lastAccess = new Map((recent.data ?? []).map((r) => [r.item.id, r.lastAccessAt]));
  const items = (recent.data ?? []).map((r) => hitToItem(r.item));
  const selection = useSelection(items);
  const [menu, setMenu] = useState<MenuState | null>(null);
  const accessedColumn = {
    label: 'Lần dùng gần nhất',
    value: (item: DriveItem) => lastAccess.get(item.id) ?? null,
  };

  return (
    <Box>
      <Typography variant="h6" component="h1" sx={{ mb: 2 }}>
        Gần đây
      </Typography>

      {recent.isError && <Alert severity="error">{toUserMessage(recent.error)}</Alert>}
      {recent.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}

      {recent.isSuccess && items.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <AccessTimeIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Chưa có tệp nào gần đây
          </Typography>
          <Typography>Tệp bạn mở, tải xuống hoặc tải lên sẽ hiện ở đây.</Typography>
        </Stack>
      )}

      {items.length > 0 && (
        <ItemTable
          items={items}
          selected={selection.selected}
          onToggle={selection.toggle}
          onToggleAll={selection.toggleAll}
          onOpen={actions.open}
          onMenu={(item, anchor) => setMenu({ item, anchor })}
          dateColumn={accessedColumn}
        />
      )}

      <ItemMenu
        state={menu}
        targets={menu ? [menu.item] : []}
        actions={actions}
        mode="view"
        onClose={() => setMenu(null)}
      />
      {actions.elements}
    </Box>
  );
}
