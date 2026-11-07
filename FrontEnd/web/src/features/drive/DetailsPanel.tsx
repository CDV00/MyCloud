import CloseIcon from '@mui/icons-material/Close';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import RestoreIcon from '@mui/icons-material/Restore';
import UploadFileOutlinedIcon from '@mui/icons-material/UploadFileOutlined';
import {
  Alert,
  Box,
  Button,
  Chip,
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
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { canEdit, deleteVersion, restoreVersion, type DriveItem, type FileVersion } from '../../api/drive';
import { useDisplayName, useUsers } from '../people/useUsers';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { formatModified, formatSize } from '../../lib/format';
import { describeActivity, describeType, formatDateTime } from './describe';
import { folderPath } from './download';
import { FileIcon } from './FileIcon';
import type { ItemActions } from './useItemActions';
import { refreshDrive, useActivity, useItem, useVersions } from './queries';

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
        {data?.kind === 'file' && <Tab value="versions" label="Phiên bản" />}
      </Tabs>

      <Box sx={{ p: 2, overflowY: 'auto', flexGrow: 1 }}>
        {item.isError && <Alert severity="error">{toUserMessage(item.error)}</Alert>}
        {item.isPending && itemId && <CircularProgress size={24} />}
        {data && activeTab === 'details' && <DetailsTabContent item={data} />}
        {data && activeTab === 'activity' && <ActivityTabContent item={data} />}
        {data && activeTab === 'versions' && <VersionsTabContent item={data} actions={actions} />}
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

function VersionsTabContent({ item, actions }: { item: DriveItem; actions: Props['actions'] }) {
  const queryClient = useQueryClient();
  const versions = useVersions(item.id);
  const restore = useMutation({
    mutationFn: (v: FileVersion) => restoreVersion(item.id, v.id),
    onSettled: () => refreshDrive(queryClient),
  });
  const remove = useMutation({
    mutationFn: (v: FileVersion) => deleteVersion(item.id, v.id),
    onSettled: () => refreshDrive(queryClient),
  });
  const error = restore.error ?? remove.error;
  const busy = restore.isPending || remove.isPending;

  return (
    <Stack spacing={2}>
      <Button
        variant="outlined"
        startIcon={<UploadFileOutlinedIcon />}
        disabled={item.trashedAt !== null || !canEdit(item)}
        onClick={() => actions.uploadVersion(item)}
        sx={{ alignSelf: 'flex-start' }}
      >
        Tải lên phiên bản mới
      </Button>
      <Typography variant="body2" color="text.secondary">
        Khôi phục một phiên bản cũ sẽ tạo phiên bản mới với nội dung đó. Các phiên bản đều được tính vào dung
        lượng.
      </Typography>
      {error && <Alert severity="error">{toUserMessage(error)}</Alert>}
      {versions.isPending && <CircularProgress size={24} />}
      {versions.data && (
        <List dense disablePadding>
          {versions.data.map((v) => (
            <ListItem
              key={v.id}
              disableGutters
              secondaryAction={
                <Stack direction="row">
                  <Tooltip title="Tải xuống phiên bản này">
                    <IconButton
                      size="small"
                      onClick={() => actions.download(item, v.id)}
                      aria-label={`Tải xuống phiên bản ${v.versionNo}`}
                    >
                      <DownloadIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  {!v.isCurrent && item.trashedAt === null && canEdit(item) && (
                    <>
                      <Tooltip title="Khôi phục phiên bản này">
                        <IconButton
                          size="small"
                          disabled={busy}
                          onClick={() => restore.mutate(v)}
                          aria-label={`Khôi phục phiên bản ${v.versionNo}`}
                        >
                          <RestoreIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      <Tooltip title="Xoá phiên bản này">
                        <IconButton
                          size="small"
                          disabled={busy}
                          onClick={() => remove.mutate(v)}
                          aria-label={`Xoá phiên bản ${v.versionNo}`}
                        >
                          <DeleteOutlineIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </>
                  )}
                </Stack>
              }
            >
              <ListItemText
                primary={
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>Phiên bản {v.versionNo}</span>
                    {v.isCurrent && <Chip label="Hiện tại" size="small" color="primary" variant="outlined" />}
                  </Stack>
                }
                secondary={`${formatDateTime(v.createdAt)} · ${formatSize(v.sizeBytes)}`}
              />
            </ListItem>
          ))}
        </List>
      )}
    </Stack>
  );
}
