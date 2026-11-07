import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlined';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ReplayIcon from '@mui/icons-material/Replay';
import {
  Box,
  CircularProgress,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Paper,
  Tooltip,
  Typography,
} from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, useSyncExternalStore } from 'react';
import { toUserMessage } from '../../api/errors';
import { formatSize } from '../../lib/format';
import { driveKeys, invalidateFolders } from '../drive/queries';
import { uploadStore, type UploadTask } from './uploadStore';

/** Bảng tiến độ upload ở góc dưới phải (giữ nguyên khi chuyển trang). */
export function UploadPanel() {
  const tasks = useSyncExternalStore(uploadStore.subscribe, uploadStore.getSnapshot);
  const queryClient = useQueryClient();
  const [collapsed, setCollapsed] = useState(false);

  // Upload xong ⇒ thư mục đích và dung lượng đổi.
  useEffect(
    () =>
      uploadStore.onDone((task) => {
        invalidateFolders(queryClient, task.parentId);
        void queryClient.invalidateQueries({ queryKey: driveKeys.quota });
      }),
    [queryClient],
  );

  if (tasks.length === 0) return null;

  const active = tasks.filter((t) => t.status === 'uploading' || t.status === 'queued').length;
  const failed = tasks.filter((t) => t.status === 'error').length;
  const title =
    active > 0
      ? `Đang tải lên ${active} mục`
      : failed > 0
        ? `${failed} mục tải lên thất bại`
        : 'Đã tải lên xong';

  return (
    <Paper
      elevation={6}
      role="region"
      aria-label="Tiến độ tải lên"
      sx={{
        position: 'fixed',
        right: 24,
        bottom: 24,
        width: 360,
        maxWidth: 'calc(100vw - 32px)',
        borderRadius: 3,
        overflow: 'hidden',
        zIndex: 1300,
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', px: 2, py: 1, bgcolor: 'background.default' }}>
        <Typography variant="subtitle2" sx={{ flexGrow: 1 }} aria-live="polite">
          {title}
        </Typography>
        <IconButton
          size="small"
          onClick={() => setCollapsed((c) => !c)}
          aria-label={collapsed ? 'Mở rộng' : 'Thu gọn'}
        >
          {collapsed ? <ExpandLessIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />}
        </IconButton>
        {active === 0 && (
          <IconButton size="small" onClick={() => uploadStore.clearFinished()} aria-label="Đóng">
            <CloseIcon fontSize="small" />
          </IconButton>
        )}
      </Box>
      {!collapsed && (
        <List dense sx={{ maxHeight: 280, overflowY: 'auto' }}>
          {tasks.map((task) => (
            <UploadRow key={task.id} task={task} />
          ))}
        </List>
      )}
    </Paper>
  );
}

function UploadRow({ task }: { task: UploadTask }) {
  const percent = task.sizeBytes === 0 ? 100 : Math.round((task.uploadedBytes / task.sizeBytes) * 100);
  const secondary =
    task.status === 'error'
      ? toUserMessage(task.error)
      : task.status === 'canceled'
        ? 'Đã huỷ'
        : task.status === 'queued'
          ? 'Đang chờ'
          : task.status === 'done'
            ? formatSize(task.sizeBytes)
            : `${formatSize(task.uploadedBytes)} / ${formatSize(task.sizeBytes)}`;

  return (
    <ListItem
      secondaryAction={
        task.status === 'uploading' || task.status === 'queued' ? (
          <Tooltip title="Huỷ">
            <IconButton
              edge="end"
              size="small"
              onClick={() => uploadStore.cancel(task.id)}
              aria-label={`Huỷ tải lên ${task.fileName}`}
            >
              <CloseIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        ) : task.status === 'error' || task.status === 'canceled' ? (
          <Tooltip title="Thử lại">
            <IconButton
              edge="end"
              size="small"
              onClick={() => uploadStore.retry(task.id)}
              aria-label={`Thử lại ${task.fileName}`}
            >
              <ReplayIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        ) : null
      }
    >
      <ListItemIcon sx={{ minWidth: 40 }}>
        {task.status === 'done' ? (
          <CheckCircleIcon color="success" />
        ) : task.status === 'error' ? (
          <ErrorOutlineIcon color="error" />
        ) : (
          <CircularProgress
            size={24}
            variant={task.status === 'uploading' ? 'determinate' : 'indeterminate'}
            value={percent}
            aria-label={`${percent}%`}
          />
        )}
      </ListItemIcon>
      <ListItemText
        primary={task.fileName}
        secondary={secondary}
        slotProps={{ primary: { noWrap: true, title: task.fileName }, secondary: { noWrap: true } }}
      />
    </ListItem>
  );
}
