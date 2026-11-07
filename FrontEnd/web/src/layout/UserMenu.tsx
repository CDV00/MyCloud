import LogoutIcon from '@mui/icons-material/Logout';
import ManageAccountsOutlinedIcon from '@mui/icons-material/ManageAccountsOutlined';
import {
  Avatar,
  Box,
  Divider,
  IconButton,
  ListItemIcon,
  Menu,
  MenuItem,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useAuth } from '../auth/authContext';
import { initials } from '../lib/format';

export function UserMenu() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);

  if (!user) return null;

  return (
    <>
      <Tooltip title={`${user.displayName} (${user.email})`}>
        <IconButton onClick={(e) => setAnchor(e.currentTarget)} aria-label="Tài khoản">
          <Avatar sx={{ width: 34, height: 34, bgcolor: 'primary.main', fontSize: 15 }}>
            {initials(user.displayName)}
          </Avatar>
        </IconButton>
      </Tooltip>
      <Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
        <Box sx={{ px: 2, py: 1 }}>
          <Typography sx={{ fontWeight: 600 }}>{user.displayName}</Typography>
          <Typography variant="body2" color="text.secondary">
            {user.email}
          </Typography>
        </Box>
        <Divider />
        <MenuItem
          onClick={() => {
            setAnchor(null);
            navigate('/account');
          }}
        >
          <ListItemIcon>
            <ManageAccountsOutlinedIcon fontSize="small" />
          </ListItemIcon>
          Quản lý tài khoản
        </MenuItem>
        <MenuItem
          onClick={() => {
            setAnchor(null);
            void logout();
          }}
        >
          <ListItemIcon>
            <LogoutIcon fontSize="small" />
          </ListItemIcon>
          Đăng xuất
        </MenuItem>
      </Menu>
    </>
  );
}
