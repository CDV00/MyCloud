import CloseIcon from '@mui/icons-material/Close';
import {
  Alert,
  Box,
  CircularProgress,
  Drawer,
  IconButton,
  Link,
  List,
  ListItem,
  ListItemText,
  Stack,
  Tab,
  Tabs,
  Tooltip,
  Typography,
} from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DriveItem } from '../../api/drive';
import { useDisplayName, useUsers } from '../people/useUsers';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { formatModified, formatSize } from '../../lib/format';
import { describeActivity, describeType, formatDateTime } from './describe';
import { folderPath } from './download';
import { FileHistoryPanel } from '../collab/FileHistoryPanel';
import { FileIcon } from './FileIcon';
import type { ItemActions } from './useItemActions';
import { useActivity, useItem } from './queries';

export type DetailsTab = 'details' | 'activity' | 'versions';

interface Props {
  itemId: string | null;
  tab: DetailsTab;
  onTabChange(tab: DetailsTab): void;
  onClose(): void;
  actions: Pick<ItemActions, 'download' | 'uploadVersion'>;
}

/** Panel bên phải: thông tin, hoạt động (từ Search — trễ vài giây), phiên bản (file). */
export function DetailsPanel({ itemId, tab, onTabChange, onClose, actions }: Props) {
  const item = useItem(itemId);
  const data = item.data;
  const activeTab = data?.kind === 'folder' && tab === 'versions' ? 'details' : tab;

  return (
    <Drawer
      anchor="right"
      open={itemId !== null}
      onClose={onClose}
      slotProps={{ paper: { sx: { width: { xs: '100%', sm: 380 } } } }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, px: 2, pt: 2 }}>
        {data && <FileIcon item={data} />}
        <Typography variant="h6" component="h2" noWrap sx={{ flexGrow: 1 }} title={data?.name}>
          {data ? (data.isRoot ? 'Drive của tôi' : data.name) : '…'}
        </Typography>
        <IconButton onClick={onClose} aria-label="Đóng thông tin chi tiết">
          <CloseIcon />
        </IconButton>
      </Box>

      <Tabs value={activeTab} onChange={(_, value: DetailsTab) => onTabChange(value)} sx={{ px: 1 }}>
        <Tab value="details" label="Chi tiết" />
        <Tab value="activity" label="Hoạt động" />
        {data?.kind === 'file' && <Tab value="versions" label="Lịch sử & trao đổi" />}
      </Tabs>

      {data && activeTab === 'versions' && (
        <Box sx={{ flexGrow: 1, minHeight: 0 }}>
          <FileHistoryPanel
            item={data}
            initialTab="versions"
            onUploadVersion={() => actions.uploadVersion(data)}
          />
        </Box>
      )}
      <Box sx={{ p: 2, overflowY: 'auto', flexGrow: activeTab === 'versions' ? 0 : 1 }}>
        {item.isError && <Alert severity="error">{toUserMessage(item.error)}</Alert>}
        {item.isPending && itemId && <CircularProgress size={24} />}
        {data && activeTab === 'details' && <DetailsTabContent item={data} />}
        {data && activeTab === 'activity' && <ActivityTabContent item={data} />}
      </Box>
    </Drawer>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="body2" component="div">
        {children}
      </Typography>
    </Box>
  );
}

function DetailsTabContent({ item }: { item: DriveItem }) {
  const parent = useItem(item.role === 'owner' ? item.parentId : null); // người được chia sẻ có thể không thấy thư mục cha
  const users = useUsers([item.ownerId, item.modifiedBy]);
  const nameOf = useDisplayName();
  return (
    <Stack spacing={2}>
      {item.trashedAt && (
        <Alert severity="warning">Đã chuyển vào thùng rác lúc {formatDateTime(item.trashedAt)}.</Alert>
      )}
      <Field label="Loại">{describeType(item)}</Field>
      {item.kind === 'file' && <Field label="Kích thước">{formatSize(item.sizeBytes)}</Field>}
      {item.parentId && item.role === 'owner' && (
        <Field label="Vị trí">
          {parent.data ? (
            <Link component={RouterLink} to={folderPath(parent.data)} underline="hover">
              {parent.data.isRoot ? 'Drive của tôi' : parent.data.name}
            </Link>
          ) : (
            '…'
          )}
        </Field>
      )}
      <Field label="Chủ sở hữu">{nameOf(item.ownerId, users)}</Field>
      <Field label="Quyền của bạn">{roleText[item.role]}</Field>
      <Field label="Sửa đổi lần cuối">
        {formatDateTime(item.modifiedAt)} · {nameOf(item.modifiedBy, users)}
      </Field>
      <Field label="Ngày tạo">{formatDateTime(item.createdAt)}</Field>
      {item.kind === 'file' && <Field label="Phiên bản hiện tại">{item.versionNo}</Field>}
      <Field label="Gắn dấu sao">{item.starred ? 'Có' : 'Không'}</Field>
    </Stack>
  );
}

const roleText: Record<DriveItem['role'], string> = {
  owner: 'Chủ sở hữu',
  editor: 'Người chỉnh sửa',
  commenter: 'Người nhận xét',
  viewer: 'Người xem',
  none: '—',
};

function ActivityTabContent({ item }: { item: DriveItem }) {
  const { user } = useAuth();
  const activity = useActivity(item.id);
  const users = useUsers((activity.data ?? []).map((a) => a.actorId));

  if (activity.isPending) return <CircularProgress size={24} />;
  if (activity.isError) return <Alert severity="error">{toUserMessage(activity.error)}</Alert>;

  return (
    <>
      {activity.data.length === 0 ? (
        <Typography color="text.secondary">Chưa có hoạt động nào.</Typography>
      ) : (
        <List dense disablePadding>
          {activity.data.map((entry) => (
            <ListItem key={entry.id} disableGutters alignItems="flex-start">
              <ListItemText
                primary={describeActivity(entry, user?.id, users.get(entry.actorId)?.displayName)}
                secondary={
                  <Tooltip title={formatDateTime(entry.at)}>
                    <span>{formatModified(entry.at)}</span>
                  </Tooltip>
                }
              />
            </ListItem>
          ))}
        </List>
      )}
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 2 }}>
        Hoạt động có thể hiển thị chậm vài giây sau khi thay đổi.
      </Typography>
    </>
  );
}
