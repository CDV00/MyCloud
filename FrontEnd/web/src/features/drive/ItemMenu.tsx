import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteForeverOutlinedIcon from '@mui/icons-material/DeleteForeverOutlined';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import DriveFileMoveOutlinedIcon from '@mui/icons-material/DriveFileMoveOutlined';
import DriveFileRenameOutlineIcon from '@mui/icons-material/DriveFileRenameOutline';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import HistoryIcon from '@mui/icons-material/History';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import PersonAddAltOutlinedIcon from '@mui/icons-material/PersonAddAltOutlined';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import StarIcon from '@mui/icons-material/Star';
import UploadFileOutlinedIcon from '@mui/icons-material/UploadFileOutlined';
import VisibilityOutlinedIcon from '@mui/icons-material/VisibilityOutlined';
import { Divider, IconButton, ListItemIcon, Menu, MenuItem, Paper, Tooltip, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { canEdit, canManage, type DriveItem } from '../../api/drive';
import { editKind } from '../preview/previewKind';
import type { ItemActions } from './useItemActions';

export interface MenuState {
  item: DriveItem;
  anchor: HTMLElement | { top: number; left: number };
}

/**
 * Menu thao tác (chuột phải / nút ⋮). `mode`:
 * "drive" — đầy đủ; "trash" — khôi phục / xoá vĩnh viễn; "view" — kết quả từ Search (mở, mở vị trí, chi tiết).
 */
export function ItemMenu({
  state,
  targets,
  actions,
  mode = 'drive',
  onClose,
}: {
  state: MenuState | null;
  targets: DriveItem[];
  actions: ItemActions;
  mode?: 'drive' | 'trash' | 'view';
  onClose(): void;
}) {
  const item = state?.item;
  const single = targets.length === 1;
  const allStarred = targets.every((t) => t.starred);
  const anchorProps =
    state && 'top' in state.anchor
      ? { anchorReference: 'anchorPosition' as const, anchorPosition: state.anchor }
      : { anchorEl: state?.anchor as HTMLElement | undefined };

  const entry = (icon: ReactNode, label: string, onClick: () => void, key = label) => (
    <MenuItem
      key={key}
      onClick={() => {
        onClose();
        onClick();
      }}
    >
      <ListItemIcon>{icon}</ListItemIcon>
      {label}
    </MenuItem>
  );

  const entries: ReactNode[] = [];
  if (item && mode === 'trash') {
    entries.push(
      entry(<RestoreFromTrashIcon fontSize="small" />, 'Khôi phục', () => actions.restore(targets)),
      entry(<DeleteForeverOutlinedIcon fontSize="small" />, 'Xoá vĩnh viễn', () => actions.purge(targets)),
    );
    if (single)
      entries.push(
        entry(<InfoOutlinedIcon fontSize="small" />, 'Thông tin chi tiết', () => actions.details(item)),
      );
  } else if (item) {
    if (single) {
      entries.push(
        item.kind === 'folder'
          ? entry(<FolderOpenIcon fontSize="small" />, 'Mở', () => actions.open(item))
          : entry(<VisibilityOutlinedIcon fontSize="small" />, 'Xem trước', () => actions.open(item)),
      );
      if (mode !== 'trash' && item.kind === 'file' && canEdit(item) && editKind(item)) {
        entries.push(entry(<EditOutlinedIcon fontSize="small" />, 'Chỉnh sửa', () => actions.edit(item)));
      }
    }

    // Thư mục / nhiều mục: tệp zip tạo trong nền (Phase 5).
    entries.push(entry(<DownloadIcon fontSize="small" />, 'Tải xuống', () => actions.downloadMany(targets)));

    if (mode === 'view') {
      if (single) {
        entries.push(
          entry(<PersonAddAltOutlinedIcon fontSize="small" />, 'Chia sẻ', () => actions.share(item)),
          entry(
            <FolderOpenIcon fontSize="small" />,
            'Mở vị trí tệp',
            () => actions.showLocation(item),
            'location',
          ),
          entry(<InfoOutlinedIcon fontSize="small" />, 'Thông tin chi tiết', () => actions.details(item)),
        );
      }
    } else {
      // Thao tác hiện theo vai trò (người xem: mở / chia sẻ (xem danh sách) / sao chép / gắn sao; thùng rác: chủ sở hữu).
      const editable = targets.every(canEdit);
      const manageable = targets.every(canManage);
      if (single) {
        entries.push(
          entry(<PersonAddAltOutlinedIcon fontSize="small" />, 'Chia sẻ', () => actions.share(item)),
        );
        if (editable) {
          entries.push(
            entry(<DriveFileRenameOutlineIcon fontSize="small" />, 'Đổi tên', () => actions.rename(item)),
          );
        }

        entries.push(entry(<ContentCopyIcon fontSize="small" />, 'Tạo bản sao', () => actions.copy(item)));
      }

      if (editable) {
        entries.push(
          entry(<DriveFileMoveOutlinedIcon fontSize="small" />, 'Di chuyển', () => actions.move(targets)),
        );
      }

      entries.push(
        allStarred
          ? entry(<StarIcon fontSize="small" />, 'Bỏ gắn dấu sao', () => actions.star(targets, false))
          : entry(<StarBorderIcon fontSize="small" />, 'Gắn dấu sao', () => actions.star(targets, true)),
      );

      if (single) {
        entries.push(
          entry(<InfoOutlinedIcon fontSize="small" />, 'Thông tin chi tiết', () => actions.details(item)),
        );
        if (item.kind === 'file') {
          entries.push(
            entry(<HistoryIcon fontSize="small" />, 'Lịch sử & trao đổi', () =>
              actions.details(item, 'versions'),
            ),
          );
          if (editable) {
            entries.push(
              entry(<UploadFileOutlinedIcon fontSize="small" />, 'Tải lên phiên bản mới', () =>
                actions.uploadVersion(item),
              ),
            );
          }
        }
      }

      if (manageable) {
        entries.push(
          <Divider key="divider" />,
          entry(<DeleteOutlineIcon fontSize="small" />, 'Chuyển vào thùng rác', () => actions.trash(targets)),
        );
      }
    }
  }

  return (
    <Menu open={state !== null} onClose={onClose} {...anchorProps}>
      {entries}
    </Menu>
  );
}

/** Thanh thao tác khi đang chọn; chiều cao cố định để bảng không bị đẩy xuống (nhấp đúp không lệch dòng). */
export function SelectionBar({
  count,
  hint,
  onClear,
  children,
}: {
  count: number;
  hint: string;
  onClear(): void;
  children?: ReactNode;
}) {
  if (count === 0) {
    return (
      <Typography
        variant="body2"
        color="text.secondary"
        sx={{ height: 40, mb: 2, display: 'flex', alignItems: 'center', px: 1 }}
      >
        {hint}
      </Typography>
    );
  }

  return (
    <Paper
      variant="outlined"
      sx={{
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        px: 1,
        height: 40,
        mb: 2,
        borderRadius: 5,
        bgcolor: 'background.default',
      }}
    >
      <IconButton size="small" onClick={onClear} aria-label="Bỏ chọn">
        <CloseIcon fontSize="small" />
      </IconButton>
      <Typography variant="body2" sx={{ mr: 1 }}>
        Đã chọn {count}
      </Typography>
      {children}
    </Paper>
  );
}

/** Nút biểu tượng có tooltip cho thanh thao tác. */
export function BarButton({
  title,
  onClick,
  children,
}: {
  title: string;
  onClick(): void;
  children: ReactNode;
}) {
  return (
    <Tooltip title={title}>
      <IconButton size="small" onClick={onClick} aria-label={title}>
        {children}
      </IconButton>
    </Tooltip>
  );
}
