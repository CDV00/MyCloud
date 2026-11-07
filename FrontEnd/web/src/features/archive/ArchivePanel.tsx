import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlined';
import FolderZipOutlinedIcon from '@mui/icons-material/FolderZipOutlined';
import {
  Button,
  CircularProgress,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Paper,
  Typography,
} from '@mui/material';
import { useSyncExternalStore } from 'react';
import { toUserMessage } from '../../api/errors';
import { archiveStore, type ArchiveTask } from './archiveStore';

function describe(task: ArchiveTask) {
  switch (task.status) {
    case 'requesting':
      return 'Đang gửi yêu cầu…';
    case 'pending':
    case 'building':
      return task.fileCount > 0 ? `Đang nén ${task.fileCount} tệp…` : 'Đang nén…';
    case 'ready':
      return task.skippedCount > 0
        ? `Đã tải xuống (${task.skippedCount} tệp đã bị xoá nên không có trong zip)`
        : 'Đã bắt đầu tải xuống';
    case 'expired':
      return 'Tệp zip đã hết hạn';
    case 'failed':
      return 'Không tạo được tệp zip';
    default:
      return toUserMessage(task.error);
  }
}

/** Tiến trình tạo tệp zip ở góc dưới trái (giữ khi chuyển trang). */
export function ArchivePanel() {
  const tasks = useSyncExternalStore(archiveStore.subscribe, archiveStore.getSnapshot);
  if (tasks.length === 0) return null;

  return (
    <Paper
      elevation={6}
      role="region"
      aria-label="Tải xuống dạng zip"
      sx={{
        position: 'fixed',
        left: 24,
        bottom: 24,
        width: 340,
        maxWidth: 'calc(100vw - 32px)',
        borderRadius: 3,
        zIndex: 1300,
      }}
    >
      <Typography variant="subtitle2" sx={{ px: 2, pt: 1.5 }}>
        Tải xuống dạng zip
      </Typography>
      <List dense aria-live="polite">
        {tasks.map((task) => {
          const busy =
            task.status === 'requesting' || task.status === 'pending' || task.status === 'building';
          const failed = task.status === 'error' || task.status === 'failed' || task.status === 'expired';
          return (
            <ListItem
              key={task.key}
              secondaryAction={
                <IconButton
                  edge="end"
                  size="small"
                  aria-label="Đóng"
                  onClick={() => archiveStore.dismiss(task.key)}
                >
                  <CloseIcon fontSize="small" />
                </IconButton>
              }
            >
              <ListItemIcon sx={{ minWidth: 36 }}>
                {busy ? (
                  <CircularProgress size={20} />
                ) : failed ? (
                  <ErrorOutlineIcon color="error" />
                ) : task.status === 'ready' ? (
                  <CheckCircleIcon color="success" />
                ) : (
                  <FolderZipOutlinedIcon />
                )}
              </ListItemIcon>
              <ListItemText
                primary={task.name}
                secondary={
                  task.status === 'ready' && task.archiveId ? (
                    <>
                      {describe(task)} ·{' '}
                      <Button
                        size="small"
                        sx={{ p: 0, minWidth: 0 }}
                        onClick={() => void archiveStore.download(task.archiveId!)}
                      >
                        Tải lại
                      </Button>
                    </>
                  ) : (
                    describe(task)
                  )
                }
                slotProps={{ primary: { noWrap: true } }}
              />
            </ListItem>
          );
        })}
      </List>
    </Paper>
  );
}
