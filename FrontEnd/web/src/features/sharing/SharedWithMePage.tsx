import FolderSharedOutlinedIcon from '@mui/icons-material/FolderSharedOutlined';
import {
  Alert,
  Box,
  Skeleton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { getSharedWithMe, type SharedHit } from '../../api/search';
import { toUserMessage } from '../../api/errors';
import { formatModified, formatSize } from '../../lib/format';
import { FileIcon } from '../drive/FileIcon';
import { ItemMenu, type MenuState } from '../drive/ItemMenu';
import { searchKeys } from '../drive/queries';
import { useItemActions } from '../drive/useItemActions';
import { useUsers } from '../people/useUsers';
import { hitToItem } from '../search/hits';
import { roleLabels } from './roles';

/** "Được chia sẻ với tôi": item người khác chia sẻ trực tiếp cho tôi (Search service, cập nhật trễ vài giây). */
export default function SharedWithMePage() {
  const shared = useQuery({ queryKey: [...searchKeys.all, 'shared-with-me'], queryFn: getSharedWithMe });
  const users = useUsers((shared.data ?? []).map((s) => s.sharedBy));
  const actions = useItemActions();
  const [menu, setMenu] = useState<MenuState | null>(null);
  const toItem = (s: SharedHit) => hitToItem(s.item, s.role);

  return (
    <Box>
      <Typography variant="h6" component="h1" sx={{ mb: 2 }}>
        Được chia sẻ với tôi
      </Typography>

      {shared.isError && <Alert severity="error">{toUserMessage(shared.error)}</Alert>}
      {shared.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}

      {shared.isSuccess && shared.data.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <FolderSharedOutlinedIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Chưa có gì được chia sẻ với bạn
          </Typography>
          <Typography>Tệp và thư mục người khác chia sẻ với bạn sẽ hiện ở đây.</Typography>
        </Stack>
      )}

      {shared.data && shared.data.length > 0 && (
        <Table size="small" aria-label="Được chia sẻ với tôi" sx={{ tableLayout: 'fixed' }}>
          <TableHead>
            <TableRow>
              <TableCell>Tên</TableCell>
              <TableCell sx={{ width: 180, display: { xs: 'none', sm: 'table-cell' } }}>
                Người chia sẻ
              </TableCell>
              <TableCell sx={{ width: 140, display: { xs: 'none', md: 'table-cell' } }}>
                Ngày chia sẻ
              </TableCell>
              <TableCell sx={{ width: 140, display: { xs: 'none', md: 'table-cell' } }}>Quyền</TableCell>
              <TableCell sx={{ width: 100, display: { xs: 'none', lg: 'table-cell' } }}>Kích thước</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {shared.data.map((s) => {
              const item = toItem(s);
              return (
                <TableRow
                  key={s.item.id}
                  hover
                  onDoubleClick={() => actions.open(item)}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    setMenu({ item, anchor: { top: e.clientY, left: e.clientX } });
                  }}
                  sx={{ cursor: 'default', userSelect: 'none' }}
                >
                  <TableCell>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
                      <FileIcon item={item} fontSize="small" />
                      <Typography noWrap variant="body2" title={s.item.name}>
                        {s.item.name}
                      </Typography>
                    </Box>
                  </TableCell>
                  <TableCell sx={{ display: { xs: 'none', sm: 'table-cell' }, color: 'text.secondary' }}>
                    {users.get(s.sharedBy)?.displayName ?? '…'}
                  </TableCell>
                  <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, color: 'text.secondary' }}>
                    {formatModified(s.sharedAt)}
                  </TableCell>
                  <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, color: 'text.secondary' }}>
                    {roleLabels[s.role]}
                  </TableCell>
                  <TableCell sx={{ display: { xs: 'none', lg: 'table-cell' }, color: 'text.secondary' }}>
                    {s.item.kind === 'file' ? formatSize(s.item.sizeBytes) : '—'}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
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
