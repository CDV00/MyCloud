import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import CloseIcon from '@mui/icons-material/Close';
import CloudUploadOutlinedIcon from '@mui/icons-material/CloudUploadOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import DriveFileMoveOutlinedIcon from '@mui/icons-material/DriveFileMoveOutlined';
import DriveFileRenameOutlineIcon from '@mui/icons-material/DriveFileRenameOutline';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import GridViewIcon from '@mui/icons-material/GridView';
import ViewListIcon from '@mui/icons-material/ViewList';
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  IconButton,
  Link,
  ListItemIcon,
  Menu,
  MenuItem,
  Paper,
  Select,
  Skeleton,
  Snackbar,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState, type DragEvent } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import type { DriveItem, SortField } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { uploadStore } from '../upload/uploadStore';
import { downloadItem, folderPath } from './download';
import { ItemGrid, ItemTable } from './ItemViews';
import { MoveDialog } from './MoveDialog';
import { useAncestors, useChildren, useItem, useRoot } from './queries';
import { RenameDialog } from './RenameDialog';

type ViewMode = 'list' | 'grid';

const VIEW_KEY = 'mycloud.drive.view';

function loadView(): ViewMode {
  try {
    return localStorage.getItem(VIEW_KEY) === 'grid' ? 'grid' : 'list';
  } catch {
    return 'list';
  }
}

function saveView(view: ViewMode) {
  try {
    localStorage.setItem(VIEW_KEY, view);
  } catch {
    // Chế độ riêng tư / bị chặn lưu trữ: chỉ nhớ trong phiên.
  }
}

/** "Drive của tôi": cây thư mục, sắp xếp, hai chế độ xem, chọn nhiều, menu chuột phải, kéo thả để upload. */
export default function DrivePage() {
  const navigate = useNavigate();
  const { folderId: routeFolderId } = useParams();
  const root = useRoot();
  const folderId = routeFolderId ?? root.data?.id;

  const folder = useItem(folderId);
  const ancestors = useAncestors(routeFolderId);
  const [sort, setSort] = useState<SortField>('name');
  const [desc, setDesc] = useState(false);
  const [view, setView] = useState<ViewMode>(loadView);
  const children = useChildren(folderId, sort, desc);

  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const [menu, setMenu] = useState<{
    item: DriveItem;
    anchor: HTMLElement | { top: number; left: number };
  } | null>(null);
  const [renaming, setRenaming] = useState<DriveItem | null>(null);
  const [moving, setMoving] = useState<DriveItem[]>([]);
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const items = children.data?.pages.flatMap((p) => p.items) ?? [];
  const selectedItems = items.filter((i) => selected.has(i.id));
  const loadError = root.error ?? folder.error ?? children.error;

  // Đổi thư mục ⇒ bỏ chọn.
  const [selectionFolder, setSelectionFolder] = useState(folderId);
  if (selectionFolder !== folderId) {
    setSelectionFolder(folderId);
    setSelected(new Set());
  }

  function toggle(item: DriveItem, additive: boolean) {
    setSelected((prev) => {
      if (!additive) return prev.size === 1 && prev.has(item.id) ? new Set() : new Set([item.id]);
      const next = new Set(prev);
      if (next.has(item.id)) next.delete(item.id);
      else next.add(item.id);
      return next;
    });
  }

  function open(item: DriveItem) {
    if (item.kind === 'folder') navigate(folderPath(item));
    else void download(item);
  }

  async function download(item: DriveItem) {
    try {
      await downloadItem(item.id);
    } catch (e) {
      setError(toUserMessage(e));
    }
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    if (folderId && event.dataTransfer.files.length > 0) uploadStore.add(event.dataTransfer.files, folderId);
  }

  const single = selectedItems.length === 1 ? selectedItems[0]! : null;
  const title = folder.data ? (folder.data.isRoot ? 'Drive của tôi' : folder.data.name) : 'Drive của tôi';

  return (
    <Box
      onDragOver={(e) => {
        if (e.dataTransfer.types.includes('Files')) {
          e.preventDefault();
          setDragging(true);
        }
      }}
      onDragLeave={(e) => {
        if (e.currentTarget === e.target) setDragging(false);
      }}
      onDrop={onDrop}
      sx={{ position: 'relative', minHeight: '100%' }}
    >
      <Stack
        direction="row"
        spacing={1}
        sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 2, minHeight: 40 }}
      >
        <Breadcrumbs
          separator={<ChevronRightIcon fontSize="small" />}
          sx={{ flexGrow: 1, '& ol': { flexWrap: 'nowrap' } }}
        >
          {(ancestors.data ?? []).map((a) => (
            <Link
              key={a.id}
              component={RouterLink}
              to={folderPath(a)}
              underline="hover"
              color="inherit"
              variant="h6"
            >
              {a.isRoot ? 'Drive của tôi' : a.name}
            </Link>
          ))}
          <Typography variant="h6" component="h1" color="text.primary" noWrap>
            {title}
          </Typography>
        </Breadcrumbs>

        <Select
          size="small"
          value={sort}
          onChange={(e) => setSort(e.target.value as SortField)}
          inputProps={{ 'aria-label': 'Sắp xếp theo' }}
        >
          <MenuItem value="name">Tên</MenuItem>
          <MenuItem value="modifiedAt">Sửa đổi lần cuối</MenuItem>
          <MenuItem value="size">Kích thước</MenuItem>
        </Select>
        <Tooltip title={desc ? 'Giảm dần' : 'Tăng dần'}>
          <IconButton onClick={() => setDesc((d) => !d)} aria-label="Đảo chiều sắp xếp">
            {desc ? <ArrowDownwardIcon fontSize="small" /> : <ArrowUpwardIcon fontSize="small" />}
          </IconButton>
        </Tooltip>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={view}
          onChange={(_, v: ViewMode | null) => {
            if (v) {
              setView(v);
              saveView(v);
            }
          }}
        >
          <ToggleButton value="list" aria-label="Dạng danh sách">
            <ViewListIcon fontSize="small" />
          </ToggleButton>
          <ToggleButton value="grid" aria-label="Dạng lưới">
            <GridViewIcon fontSize="small" />
          </ToggleButton>
        </ToggleButtonGroup>
      </Stack>

      {/* Chỗ cố định cho thanh thao tác: hiện/ẩn không đẩy bảng xuống, nên nhấp đúp không bị lệch dòng. */}
      {selectedItems.length === 0 ? (
        <Typography
          variant="body2"
          color="text.secondary"
          sx={{ height: 40, mb: 2, display: 'flex', alignItems: 'center', px: 1 }}
        >
          Nhấp đúp để mở · nhấp chuột phải để xem thao tác · kéo thả tệp vào đây để tải lên
        </Typography>
      ) : (
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
          <IconButton size="small" onClick={() => setSelected(new Set())} aria-label="Bỏ chọn">
            <CloseIcon fontSize="small" />
          </IconButton>
          <Typography variant="body2" sx={{ mr: 1 }}>
            Đã chọn {selectedItems.length}
          </Typography>
          {single?.kind === 'file' && (
            <Tooltip title="Tải xuống">
              <IconButton size="small" onClick={() => void download(single)} aria-label="Tải xuống">
                <DownloadIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
          {single && (
            <Tooltip title="Đổi tên">
              <IconButton size="small" onClick={() => setRenaming(single)} aria-label="Đổi tên">
                <DriveFileRenameOutlineIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
          <Tooltip title="Di chuyển">
            <IconButton size="small" onClick={() => setMoving(selectedItems)} aria-label="Di chuyển">
              <DriveFileMoveOutlinedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Paper>
      )}

      {loadError && <Alert severity="error">{toUserMessage(loadError)}</Alert>}

      {!loadError && children.isPending && (
        <Box>
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} height={44} />
          ))}
        </Box>
      )}

      {!loadError && children.isSuccess && items.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <CloudUploadOutlinedIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Thả tệp vào đây
          </Typography>
          <Typography>hoặc dùng nút “Mới” để tạo thư mục, tải tệp lên.</Typography>
        </Stack>
      )}

      {items.length > 0 &&
        (view === 'list' ? (
          <ItemTable
            items={items}
            selected={selected}
            onToggle={toggle}
            onToggleAll={() =>
              setSelected((prev) =>
                prev.size === items.length ? new Set() : new Set(items.map((i) => i.id)),
              )
            }
            onOpen={open}
            onMenu={(item, anchor) => setMenu({ item, anchor })}
          />
        ) : (
          <ItemGrid
            items={items}
            selected={selected}
            onToggle={toggle}
            onToggleAll={() => undefined}
            onOpen={open}
            onMenu={(item, anchor) => setMenu({ item, anchor })}
          />
        ))}

      {children.hasNextPage && (
        <Box sx={{ textAlign: 'center', mt: 2 }}>
          <Button onClick={() => void children.fetchNextPage()} disabled={children.isFetchingNextPage}>
            {children.isFetchingNextPage ? 'Đang tải…' : 'Tải thêm'}
          </Button>
        </Box>
      )}

      <Menu
        open={menu !== null}
        onClose={() => setMenu(null)}
        {...(menu && 'top' in menu.anchor
          ? { anchorReference: 'anchorPosition' as const, anchorPosition: menu.anchor }
          : { anchorEl: menu?.anchor as HTMLElement | undefined })}
      >
        {menu?.item.kind === 'folder' ? (
          <MenuItem
            onClick={() => {
              open(menu.item);
              setMenu(null);
            }}
          >
            <ListItemIcon>
              <FolderOpenIcon fontSize="small" />
            </ListItemIcon>
            Mở
          </MenuItem>
        ) : (
          <MenuItem
            onClick={() => {
              void download(menu!.item);
              setMenu(null);
            }}
          >
            <ListItemIcon>
              <DownloadIcon fontSize="small" />
            </ListItemIcon>
            Tải xuống
          </MenuItem>
        )}
        <MenuItem
          onClick={() => {
            setRenaming(menu!.item);
            setMenu(null);
          }}
        >
          <ListItemIcon>
            <DriveFileRenameOutlineIcon fontSize="small" />
          </ListItemIcon>
          Đổi tên
        </MenuItem>
        <MenuItem
          onClick={() => {
            const targets =
              selected.has(menu!.item.id) && selectedItems.length > 1 ? selectedItems : [menu!.item];
            setMoving(targets);
            setMenu(null);
          }}
        >
          <ListItemIcon>
            <DriveFileMoveOutlinedIcon fontSize="small" />
          </ListItemIcon>
          Di chuyển
        </MenuItem>
      </Menu>

      <RenameDialog item={renaming} onClose={() => setRenaming(null)} />
      <MoveDialog
        items={moving}
        onClose={() => {
          setMoving([]);
          setSelected(new Set());
        }}
      />

      {dragging && (
        <Box
          sx={{
            position: 'absolute',
            inset: 0,
            border: 2,
            borderColor: 'primary.main',
            borderRadius: 3,
            bgcolor: 'rgba(11, 87, 208, 0.06)',
            display: 'grid',
            placeItems: 'center',
            pointerEvents: 'none',
          }}
        >
          <Typography variant="h6" color="primary">
            Thả tệp để tải lên “{title}”
          </Typography>
        </Box>
      )}

      <Snackbar open={error !== null} autoHideDuration={5000} onClose={() => setError(null)}>
        <Alert severity="error" onClose={() => setError(null)}>
          {error}
        </Alert>
      </Snackbar>
    </Box>
  );
}
