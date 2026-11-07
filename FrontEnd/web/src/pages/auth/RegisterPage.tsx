import { Alert, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { useState, type FormEvent } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { AuthCard } from './AuthCard';

const MIN_PASSWORD_LENGTH = 8;

export default function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const passwordTooShort = password.length > 0 && password.length < MIN_PASSWORD_LENGTH;
  const mismatch = confirm.length > 0 && confirm !== password;
  const canSubmit =
    displayName.trim() && email.trim() && password.length >= MIN_PASSWORD_LENGTH && confirm === password;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;
    setError(null);
    setSubmitting(true);
    try {
      await register(email.trim(), password, displayName.trim());
      navigate('/', { replace: true });
    } catch (e) {
      setError(toUserMessage(e));
      setSubmitting(false);
    }
  }

  return (
    <AuthCard title="Tạo tài khoản" subtitle="Lưu trữ và chia sẻ file của bạn">
      <Stack component="form" spacing={2} onSubmit={handleSubmit} noValidate>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Tên hiển thị"
          autoComplete="name"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          required
          autoFocus
          slotProps={{ htmlInput: { maxLength: 100 } }}
        />
        <TextField
          label="Email"
          type="email"
          autoComplete="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <TextField
          label="Mật khẩu"
          type="password"
          autoComplete="new-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          error={passwordTooShort}
          helperText={`Tối thiểu ${MIN_PASSWORD_LENGTH} ký tự`}
          required
        />
        <TextField
          label="Nhập lại mật khẩu"
          type="password"
          autoComplete="new-password"
          value={confirm}
          onChange={(e) => setConfirm(e.target.value)}
          error={mismatch}
          helperText={mismatch ? 'Mật khẩu nhập lại không khớp' : ' '}
          required
        />
        <Button type="submit" variant="contained" size="large" disabled={submitting || !canSubmit}>
          {submitting ? 'Đang tạo tài khoản…' : 'Tạo tài khoản'}
        </Button>
        <Typography variant="body2" sx={{ textAlign: 'center' }}>
          Đã có tài khoản?{' '}
          <Link component={RouterLink} to="/login">
            Đăng nhập
          </Link>
        </Typography>
      </Stack>
    </AuthCard>
  );
}
