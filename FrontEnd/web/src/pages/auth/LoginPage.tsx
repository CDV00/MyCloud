import { Alert, Button, Link, Stack, TextField, Typography } from '@mui/material';
import { useState, type FormEvent } from 'react';
import { Link as RouterLink, useLocation, useNavigate } from 'react-router';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { AuthCard } from './AuthCard';

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const notice = (location.state as { notice?: string } | null)?.notice;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email.trim(), password);
      const from = (location.state as { from?: string } | null)?.from;
      navigate(from?.startsWith('/') ? from : '/', { replace: true });
    } catch (e) {
      setError(toUserMessage(e));
      setSubmitting(false);
    }
  }

  return (
    <AuthCard title="Đăng nhập" subtitle="Tiếp tục tới MyCloud">
      <Stack component="form" spacing={2} onSubmit={handleSubmit} noValidate>
        {notice && <Alert severity="info">{notice}</Alert>}
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Email"
          type="email"
          autoComplete="username"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          autoFocus
        />
        <TextField
          label="Mật khẩu"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <Button type="submit" variant="contained" size="large" disabled={submitting || !email || !password}>
          {submitting ? 'Đang đăng nhập…' : 'Đăng nhập'}
        </Button>
        <Typography variant="body2" sx={{ textAlign: 'center' }}>
          Chưa có tài khoản?{' '}
          <Link component={RouterLink} to="/register">
            Tạo tài khoản
          </Link>
        </Typography>
      </Stack>
    </AuthCard>
  );
}
