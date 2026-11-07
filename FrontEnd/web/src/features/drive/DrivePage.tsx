import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import CloudUploadOutlinedIcon from '@mui/icons-material/CloudUploadOutlined';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import DriveFileMoveOutlinedIcon from '@mui/icons-material/DriveFileMoveOutlined';
import DriveFileRenameOutlineIcon from '@mui/icons-material/DriveFileRenameOutline';
import GridViewIcon from '@mui/icons-material/GridView';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import PersonAddAltOutlinedIcon from '@mui/icons-material/PersonAddAltOutlined';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import StarIcon from '@mui/icons-material/Star';
import ViewListIcon from '@mui/icons-material/ViewList';
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  IconButton,
  Link,
  MenuItem,
  Select,
  Skeleton,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState, type DragEvent, type KeyboardEvent } from 'react';
import { Link as RouterLink, useParams } from 'react-router';
import { canEdit, canManage, type SortField } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { uploadStore } from '../upload/uploadStore';
import { folderPath } from './download';
import { BarButton, ItemMenu, SelectionBar, type MenuState } from './ItemMenu';
import { ItemGrid, ItemTable } from './ItemViews';
import { useAncestors, useChildren, useItem, useRoot } from './queries';
import { useSelection } from './selection';
import { useItemActions } from './useItemActions';

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

/**
 * "Drive của tôi": cây thư mục, sắp xếp, hai chế độ xem, chọn nhiều, menu chuột phải, kéo thả để upload,
 * phím Delete để chuyển vào thùng rác. Thư mục đang ở thùng rác mở được ở chế độ chỉ xem.
 */
export default function DrivePage() {
  const { folderId: routeFolderId } = useParams();
  const root = useRoot();
  const folderId = routeFolderId ?? root.data?.id;

  const folder = useItem(folderId);
  const ancestors = useAncestors(routeFolderId);
  const [sort, setSort] = useState<SortField>('name');
  const [desc, setDesc] = useState(false);
  const [view, setView] = useState<ViewMode>(loadView);
  const children = useChildren(folderId, sort, desc);
  const actions = useItemActions();

  const items = children.data?.pages.flatMap((p) => p.items) ?? [];
  const selection = useSelection(items, folderId);
  const { selectedItems } = selection;
  const [menu, setMenu] = useState<MenuState | null>(null);
  const [dragging, setDragging] = useState(false);
  const loadError = root.error ?? folder.error ?? children.error;
  const inTrash = folder.data?.trashedAt != null;
  // Thư mục được chia sẻ chỉ quyền xem: không thả tệp / tạo mới bên trong.
  const writable = !inTrash && (folder.data ? canEdit(folder.data) : true);
  const sharedWithMe = folder.data !== undefined && folder.data.role !== 'owner';

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    if (folderId && writable && event.dataTransfer.files.length > 0) {
      uploadStore.add(event.dataTransfer.files, folderId);
    }
  }

  function trashSelection() {
    actions.trash(selectedItems);
    selection.clear();
  }

  function onKeyDown(event: KeyboardEvent) {
    const typing = event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement;
    if (
      event.key === 'Delete' &&
      !typing &&
      selectedItems.length > 0 &&
      !inTrash &&
      selectedItems.every(canManage)
    ) {
      trashSelection();
    }
  }

  const single = selectedItems.length === 1 ? selectedItems[0]! : null;
  const allStarred = selectedItems.length > 0 && selectedItems.every((i) => i.starred);
  const editable = selectedItems.length > 0 && selectedItems.every(canEdit);
  const manageable = selectedItems.length > 0 && selectedItems.every(canManage);
  const title = folder.data ? (folder.data.isRoot ? 'Drive của tôi' : folder.data.name) : 'Drive của tôi';

  return (
    <Box
      onKeyDown={onKeyDown}
      onDragOver={(e) => {
        if (e.dataTransfer.types.includes('Files') && writable) {
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
          {sharedWithMe && (
            <Link component={RouterLink} to="/shared" underline="hover" color="inherit" variant="h6">
              Được chia sẻ với tôi
            </Link>
          )}
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

      {sharedWithMe && !writable && !inTrash && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Bạn chỉ có quyền xem thư mục này.
        </Alert>
      )}

      {inTrash && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          Thư mục này đang ở thùng rác nên chỉ xem được. Khôi phục nó từ trang Thùng rác để chỉnh sửa.
        </Alert>
      )}

      <SelectionBar
        count={selectedItems.length}
        onClear={selection.clear}
        hint="Nhấp đúp để mở · nhấp chuột phải để xem thao tác · kéo thả tệp vào đây để tải lên"
      >
        {single?.kind === 'file' && (
          <BarButton title="Tải xuống" onClick={() => actions.download(single)}>
            <DownloadIcon fontSize="small" />
          </BarButton>
        )}
        {!inTrash && single && (
          <BarButton title="Chia sẻ" onClick={() => actions.share(single)}>
            <PersonAddAltOutlinedIcon fontSize="small" />
          </BarButton>
        )}
        {!inTrash && single && editable && (
          <BarButton title="Đổi tên" onClick={() => actions.rename(single)}>
            <DriveFileRenameOutlineIcon fontSize="small" />
          </BarButton>
        )}
        {!inTrash && editable && (
          <BarButton title="Di chuyển" onClick={() => actions.move(selectedItems)}>
            <DriveFileMoveOutlinedIcon fontSize="small" />
          </BarButton>
        )}
        {!inTrash && (
          <BarButton
            title={allStarred ? 'Bỏ gắn dấu sao' : 'Gắn dấu sao'}
            onClick={() => actions.star(selectedItems, !allStarred)}
          >
            {allStarred ? <StarIcon fontSize="small" /> : <StarBorderIcon fontSize="small" />}
          </BarButton>
        )}
        {!inTrash && manageable && (
          <BarButton title="Chuyển vào thùng rác" onClick={trashSelection}>
            <DeleteOutlineIcon fontSize="small" />
          </BarButton>
        )}
        {single && (
          <BarButton title="Thông tin chi tiết" onClick={() => actions.details(single)}>
            <InfoOutlinedIcon fontSize="small" />
          </BarButton>
        )}
      </SelectionBar>

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
            {writable ? 'Thả tệp vào đây' : 'Thư mục trống'}
          </Typography>
          {writable && <Typography>hoặc dùng nút “Mới” để tạo thư mục, tải tệp lên.</Typography>}
        </Stack>
      )}

      {items.length > 0 &&
        (view === 'list' ? (
          <ItemTable
            items={items}
            selected={selection.selected}
            onToggle={selection.toggle}
            onToggleAll={selection.toggleAll}
            onOpen={actions.open}
            onMenu={(item, anchor) => setMenu({ item, anchor })}
          />
        ) : (
          <ItemGrid
            items={items}
            selected={selection.selected}
            onToggle={selection.toggle}
            onToggleAll={() => undefined}
            onOpen={actions.open}
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

      <ItemMenu
        state={menu}
        targets={menu ? selection.targetsFor(menu.item) : []}
        actions={actions}
        mode={inTrash ? 'view' : 'drive'}
        onClose={() => setMenu(null)}
      />
      {actions.elements}

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
    </Box>
  );
}
