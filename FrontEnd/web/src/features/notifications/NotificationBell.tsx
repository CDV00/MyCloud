import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import FolderZipOutlinedIcon from '@mui/icons-material/FolderZipOutlined';
import FolderSharedOutlinedIcon from '@mui/icons-material/FolderSharedOutlined';
import NotificationsNoneIcon from '@mui/icons-material/NotificationsNone';
import StorageIcon from '@mui/icons-material/Storage';
import {
  Badge,
  Box,
  Button,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Snackbar,
  Alert,
  Tooltip,
  Typography,
} from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import {
  listNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type AppNotification,
} from '../../api/notifications';
import { toUserMessage } from '../../api/errors';
import { formatModified, formatSize } from '../../lib/format';
import { archiveStore } from '../archive/archiveStore';
import { liveHub } from '../live/liveHub';
import { searchKeys } from '../drive/queries';

const notificationKey = ['notifications'] as const;

/** Nội dung hiển thị của một thông báo. */
function describeNotification(n: AppNotification): string {
  switch (n.type) {
    case 'item_shared': {
      const what = n.payload.itemKind === 'folder' ? 'thư mục' : 'tệp';
      return `${n.payload.sharedByName} đã chia sẻ ${what} “${n.payload.itemName}” với bạn`;
    }
    case 'archive_ready':
      return `“${n.payload.name}” đã sẵn sàng để tải xuống (${formatSize(n.payload.sizeBytes)})`;
    case 'archive_failed':
      return `Không tạo được “${n.payload.name}”`;
    case 'comment':
      return n.payload.mentioned
        ? `${n.payload.authorName} đã nhắc đến bạn trong “${n.payload.itemName}”`
        : n.payload.versionNo
          ? `${n.payload.authorName} đã góp ý phiên bản ${n.payload.versionNo} của “${n.payload.itemName}”`
          : n.payload.isReply
            ? `${n.payload.authorName} đã trả lời trong “${n.payload.itemName}”`
            : `${n.payload.authorName} đã bình luận về “${n.payload.itemName}”`;
    default:
      return n.payload.percent >= 100
        ? 'Bạn đã dùng hết dung lượng lưu trữ'
        : `Bạn đã dùng ${n.payload.percent}% dung lượng lưu trữ`;
  }
}

function secondaryText(n: AppNotification): string {
  if (n.type === 'item_shared' && n.payload.message)
    return `“${n.payload.message}” · ${formatModified(n.createdAt)}`;
  if (n.type === 'comment') return `“${n.payload.excerpt}” · ${formatModified(n.createdAt)}`;
  if (n.type === 'archive_ready') {
    return new Date(n.payload.expiresAt) < new Date()
      ? `Đã hết hạn · ${formatModified(n.createdAt)}`
      : `Bấm để tải xuống · hết hạn ${formatModified(n.payload.expiresAt)}`;
  }

  return formatModified(n.createdAt);
}

/**
 * Chuông thông báo: danh sách + số chưa đọc; nhận thông báo mới tức thì qua SignalR (tự kết nối lại). Token gửi qua
 * accessTokenFactory — SignalR đặt nó vào header khi negotiate và vào query khi mở WebSocket.
 */
export function NotificationBell() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const notifications = useQuery({ queryKey: notificationKey, queryFn: listNotifications });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: notificationKey });
  const markRead = useMutation({ mutationFn: markNotificationRead, onSettled: refresh });
  const markAll = useMutation({ mutationFn: markAllNotificationsRead, onSettled: refresh });

  useEffect(
    () =>
      liveHub.onNotification((n) => {
        setToast(describeNotification(n));
        void queryClient.invalidateQueries({ queryKey: notificationKey });
        if (n.type === 'item_shared') {
          // "Được chia sẻ với tôi" cập nhật khi Search nhận event (trễ vài giây).
          setTimeout(() => void queryClient.invalidateQueries({ queryKey: searchKeys.all }), 1500);
        }
      }),
    [queryClient],
  );

  const items = notifications.data?.items ?? [];
  const unread = notifications.data?.unreadCount ?? 0;

  function open(n: AppNotification) {
    setAnchor(null);
    if (!n.read) markRead.mutate(n.id);
    if (n.type === 'item_shared') {
      navigate(n.payload.itemKind === 'folder' ? `/my-drive/folders/${n.payload.itemId}` : '/shared');
    } else if (n.type === 'archive_ready') {
      archiveStore.download(n.payload.archiveId).catch((e: unknown) => setToast(toUserMessage(e)));
    } else if (n.type === 'comment') {
      navigate(`/files/${n.payload.itemId}?panel=history`);
    } else if (n.type === 'quota') {
      navigate('/storage');
    }
  }

  return (
    <>
      <Tooltip title="Thông báo">
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label={`Thông báo (${unread} chưa đọc)`}>
          <Badge badgeContent={unread} color="error" max={99}>
            <NotificationsNoneIcon />
          </Badge>
        </IconButton>
      </Tooltip>
      <Menu
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => setAnchor(null)}
        slotProps={{ paper: { sx: { width: 380, maxWidth: '100vw', maxHeight: 480 } } }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', px: 2, py: 1 }}>
          <Typography variant="subtitle1" sx={{ flexGrow: 1, fontWeight: 600 }}>
            Thông báo
          </Typography>
          <Button size="small" disabled={unread === 0} onClick={() => markAll.mutate()}>
            Đánh dấu đã đọc tất cả
          </Button>
        </Box>
        {items.length === 0 && (
          <Typography color="text.secondary" sx={{ px: 2, py: 3, textAlign: 'center' }}>
            Chưa có thông báo nào
          </Typography>
        )}
        {items.map((n) => (
          <MenuItem
            key={n.id}
            onClick={() => open(n)}
            sx={{ whiteSpace: 'normal', alignItems: 'flex-start', py: 1.5 }}
          >
            <ListItemIcon sx={{ mt: 0.5 }}>
              {n.type === 'item_shared' ? (
                <FolderSharedOutlinedIcon fontSize="small" />
              ) : n.type === 'comment' ? (
                <ForumOutlinedIcon fontSize="small" />
              ) : n.type === 'archive_ready' || n.type === 'archive_failed' ? (
                <FolderZipOutlinedIcon
                  fontSize="small"
                  color={n.type === 'archive_failed' ? 'error' : undefined}
                />
              ) : (
                <StorageIcon fontSize="small" />
              )}
            </ListItemIcon>
            <ListItemText
              primary={describeNotification(n)}
              secondary={secondaryText(n)}
              slotProps={{ primary: { sx: { fontWeight: n.read ? 400 : 600 } } }}
            />
            {!n.read && (
              <Box
                sx={{
                  width: 8,
                  height: 8,
                  borderRadius: '50%',
                  bgcolor: 'primary.main',
                  mt: 1,
                  ml: 1,
                  flexShrink: 0,
                }}
              />
            )}
          </MenuItem>
        ))}
      </Menu>
      <Snackbar
        open={toast !== null}
        autoHideDuration={6000}
        onClose={() => setToast(null)}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
      >
        <Alert severity="info" onClose={() => setToast(null)}>
          {toast}
        </Alert>
      </Snackbar>
    </>
  );
}
