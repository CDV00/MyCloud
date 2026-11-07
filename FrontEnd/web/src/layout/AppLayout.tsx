import AccessTimeIcon from '@mui/icons-material/AccessTime';
import CloudIcon from '@mui/icons-material/Cloud';
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import CreateNewFolderOutlinedIcon from '@mui/icons-material/CreateNewFolderOutlined';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import FolderSharedOutlinedIcon from '@mui/icons-material/FolderSharedOutlined';
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined';
import MenuIcon from '@mui/icons-material/Menu';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import StorageIcon from '@mui/icons-material/Storage';
import UploadFileOutlinedIcon from '@mui/icons-material/UploadFileOutlined';
import AddIcon from '@mui/icons-material/Add';
import {
  AppBar,
  Box,
  Button,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import { useRef, useState, type ReactNode } from 'react';
import { NavLink, Outlet, useMatch } from 'react-router';
import { NewFolderDialog } from '../features/drive/NewFolderDialog';
import { SearchBox } from '../features/search/SearchBox';
import { QuotaBar } from '../features/drive/QuotaBar';
import { useRoot } from '../features/drive/queries';
import { UploadPanel } from '../features/upload/UploadPanel';
import { uploadStore } from '../features/upload/uploadStore';
import { UserMenu } from './UserMenu';

const DRAWER_WIDTH = 256;

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  end?: boolean;
}

// Mục chưa có chức năng dẫn tới trang giới thiệu phase sẽ làm (docs/07).
const navItems: NavItem[] = [
  { to: '/', label: 'Trang chủ', icon: <HomeOutlinedIcon />, end: true },
  { to: '/my-drive', label: 'Drive của tôi', icon: <CloudQueueIcon /> },
  { to: '/shared', label: 'Được chia sẻ với tôi', icon: <FolderSharedOutlinedIcon /> },
  { to: '/recent', label: 'Gần đây', icon: <AccessTimeIcon /> },
  { to: '/starred', label: 'Có gắn dấu sao', icon: <StarBorderIcon /> },
  { to: '/trash', label: 'Thùng rác', icon: <DeleteOutlineIcon /> },
  { to: '/storage', label: 'Bộ nhớ', icon: <StorageIcon /> },
];

export function AppLayout() {
  const theme = useTheme();
  const isDesktop = useMediaQuery(theme.breakpoints.up('md'));
  const [mobileOpen, setMobileOpen] = useState(false);
  const [newMenuAnchor, setNewMenuAnchor] = useState<HTMLElement | null>(null);
  const [newFolderOpen, setNewFolderOpen] = useState(false);

  const fileInput = useRef<HTMLInputElement>(null);

  // Thư mục mới / file tải lên vào thư mục đang mở; ở các trang khác thì vào "Drive của tôi".
  const folderMatch = useMatch('/my-drive/folders/:folderId');
  const root = useRoot();
  const currentFolderId = folderMatch?.params.folderId ?? root.data?.id ?? null;

  const navigation = (
    <Box sx={{ px: 1.5, py: 2 }}>
      <Button
        variant="contained"
        color="inherit"
        startIcon={<AddIcon />}
        onClick={(e) => setNewMenuAnchor(e.currentTarget)}
        sx={{ bgcolor: 'background.paper', borderRadius: 4, px: 3, py: 1.5, mb: 2, boxShadow: 1 }}
      >
        Mới
      </Button>
      <List component="nav" aria-label="Điều hướng chính" dense>
        {navItems.map((item) => (
          <ListItemButton
            key={item.to}
            component={NavLink}
            to={item.to}
            end={item.end}
            onClick={() => setMobileOpen(false)}
            sx={{
              borderRadius: 5,
              mb: 0.25,
              '&.active': { bgcolor: 'primary.light', color: 'primary.contrastText', fontWeight: 600 },
            }}
          >
            <ListItemIcon sx={{ minWidth: 40, color: 'inherit' }}>{item.icon}</ListItemIcon>
            <ListItemText primary={item.label} />
          </ListItemButton>
        ))}
      </List>
      <QuotaBar />
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar
        position="fixed"
        color="inherit"
        elevation={0}
        sx={{ zIndex: (t) => t.zIndex.drawer + 1, bgcolor: 'background.default' }}
      >
        <Toolbar>
          {!isDesktop && (
            <IconButton edge="start" aria-label="Mở menu" onClick={() => setMobileOpen(true)} sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <CloudIcon color="primary" sx={{ mr: 1 }} />
          <Typography
            variant="h6"
            component="div"
            sx={{ width: { md: DRAWER_WIDTH - 48 }, mr: 2, display: { xs: 'none', sm: 'block' } }}
          >
            MyCloud
          </Typography>
          <SearchBox />
          <Box sx={{ flexGrow: 1, minWidth: 8 }} />
          <UserMenu />
        </Toolbar>
      </AppBar>

      <Drawer
        variant={isDesktop ? 'permanent' : 'temporary'}
        open={isDesktop || mobileOpen}
        onClose={() => setMobileOpen(false)}
        sx={{
          width: DRAWER_WIDTH,
          flexShrink: 0,
          '& .MuiDrawer-paper': { width: DRAWER_WIDTH, border: 0, bgcolor: 'background.default' },
        }}
      >
        <Toolbar />
        {navigation}
      </Drawer>

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 1, md: 2 } }}>
        <Toolbar />
        <Box
          sx={{
            bgcolor: 'background.paper',
            borderRadius: 4,
            minHeight: 'calc(100vh - 96px)',
            p: { xs: 2, md: 3 },
          }}
        >
          <Outlet />
        </Box>
      </Box>

      <Menu anchorEl={newMenuAnchor} open={Boolean(newMenuAnchor)} onClose={() => setNewMenuAnchor(null)}>
        <MenuItem
          onClick={() => {
            setNewMenuAnchor(null);
            setNewFolderOpen(true);
          }}
        >
          <ListItemIcon>
            <CreateNewFolderOutlinedIcon fontSize="small" />
          </ListItemIcon>
          Thư mục mới
        </MenuItem>
        <MenuItem
          disabled={!currentFolderId}
          onClick={() => {
            setNewMenuAnchor(null);
            fileInput.current?.click();
          }}
        >
          <ListItemIcon>
            <UploadFileOutlinedIcon fontSize="small" />
          </ListItemIcon>
          Tải tệp lên
        </MenuItem>
      </Menu>

      <input
        ref={fileInput}
        type="file"
        multiple
        hidden
        aria-hidden
        onChange={(e) => {
          if (e.target.files && currentFolderId) uploadStore.add(e.target.files, currentFolderId);
          e.target.value = ''; // chọn lại cùng file vẫn kích hoạt onChange
        }}
      />

      <UploadPanel />

      <NewFolderDialog
        open={newFolderOpen}
        parentId={currentFolderId}
        onClose={() => setNewFolderOpen(false)}
      />
    </Box>
  );
}
