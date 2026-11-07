import { Alert, Button, Divider, Stack, TextField, Typography } from '@mui/material';
import { useMutation } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { changePassword, updateMe } from '../api/auth';
import { toUserMessage } from '../api/errors';
import { useAuth } from '../auth/authContext';

export default function AccountPage() {
  const { user, reloadProfile } = useAuth();
  const navigate = useNavigate();

  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');

  const profile = useMutation({
    mutationFn: () => updateMe(displayName.trim()),
    onSuccess: () => reloadProfile(),
  });
  const password = useMutation({
    mutationFn: () => changePassword(currentPassword, newPassword),
    onSuccess: () =>
      navigate('/login', {
        replace: true,
        state: { notice: 'Đã đổi mật khẩu. Mọi phiên đăng nhập đã được đăng xuất — hãy đăng nhập lại.' },
      }),
  });

  function saveProfile(event: FormEvent) {
    event.preventDefault();
    profile.mutate();
  }

  function savePassword(event: FormEvent) {
    event.preventDefault();
    password.mutate();
  }

  return (
    <Stack spacing={3} sx={{ maxWidth: 480 }}>
      <Typography variant="h5" component="h1">
        Quản lý tài khoản
      </Typography>

      <Stack component="form" spacing={2} onSubmit={saveProfile}>
        <Typography variant="h6">Hồ sơ</Typography>
        <TextField label="Email" value={user?.email ?? ''} disabled />
        <TextField
          label="Tên hiển thị"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
        {profile.isSuccess && <Alert severity="success">Đã lưu.</Alert>}
        {profile.isError && <Alert severity="error">{toUserMessage(profile.error)}</Alert>}
        <Button type="submit" variant="contained" disabled={!displayName.trim() || profile.isPending}>
          Lưu
        </Button>
      </Stack>

      <Divider />

      <Stack component="form" spacing={2} onSubmit={savePassword}>
        <Typography variant="h6">Đổi mật khẩu</Typography>
        <TextField
          label="Mật khẩu hiện tại"
          type="password"
          autoComplete="current-password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
        />
        <TextField
          label="Mật khẩu mới"
          type="password"
          autoComplete="new-password"
          helperText="Tối thiểu 8 ký tự. Mọi thiết bị khác sẽ bị đăng xuất."
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
        />
        {password.isError && <Alert severity="error">{toUserMessage(password.error)}</Alert>}
        <Button
          type="submit"
          variant="outlined"
          disabled={!currentPassword || newPassword.length < 8 || password.isPending}
        >
          Đổi mật khẩu
        </Button>
      </Stack>
    </Stack>
  );
}
