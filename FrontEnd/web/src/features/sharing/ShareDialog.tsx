import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import LinkIcon from '@mui/icons-material/Link';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
  List,
  ListItem,
  ListItemAvatar,
  ListItemText,
  MenuItem,
  Select,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import {
  changePermission,
  createLink,
  disableLink,
  getSharing,
  linkUrl,
  revokePermission,
  shareItem,
  type DriveItem,
  type GrantRole,
} from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { initials } from '../../lib/format';
import { refreshDrive } from '../drive/queries';
import { roleLabels } from './roles';

interface Props {
  item: Pick<DriveItem, 'id' | 'name'> | null;
  onClose(): void;
}

/** Chia sẻ: thêm người theo email, đổi vai trò / thu hồi, link "Bất kỳ ai có đường liên kết" (chỉ xem). */
export function ShareDialog({ item, onClose }: Props) {
  return item ? <ShareForm key={item.id} item={item} onClose={onClose} /> : null;
}

function ShareForm({ item, onClose }: { item: Pick<DriveItem, 'id' | 'name'>; onClose(): void }) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const key = ['sharing', item.id];
  const sharing = useQuery({ queryKey: key, queryFn: () => getSharing(item.id) });
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<GrantRole>('viewer');
  const [notify, setNotify] = useState(true);
  const [message, setMessage] = useState('');
  const [linkPassword, setLinkPassword] = useState('');
  const [newLink, setNewLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: key });
    refreshDrive(queryClient);
  };
  const add = useMutation({
    mutationFn: () =>
      shareItem(item.id, { email: email.trim(), role, notify, message: message.trim() || undefined }),
    onSuccess: () => {
      setEmail('');
      setMessage('');
    },
    onSettled: refresh,
  });
  const change = useMutation({
    mutationFn: (p: { id: string; role: GrantRole }) => changePermission(item.id, p.id, p.role),
    onSettled: refresh,
  });
  const revoke = useMutation({
    mutationFn: (id: string) => revokePermission(item.id, id),
    onSettled: refresh,
  });
  const makeLink = useMutation({
    mutationFn: () => createLink(item.id, { role: 'viewer', password: linkPassword || undefined }),
    onSuccess: (created) => {
      setNewLink(linkUrl(created.token));
      setLinkPassword('');
      setCopied(false);
    },
    onSettled: refresh,
  });
  const removeLink = useMutation({
    mutationFn: (id: string) => disableLink(item.id, id),
    onSettled: refresh,
  });

  const error =
    add.error ?? change.error ?? revoke.error ?? makeLink.error ?? removeLink.error ?? sharing.error;
  const data = sharing.data;
  const canShare = data?.canShare ?? false;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (email.trim()) add.mutate();
  }

  async function copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
    } catch {
      // http://<IP> không phải secure context ⇒ không có clipboard API: người dùng tự chọn và chép.
      setCopied(false);
    }
  }

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Chia sẻ “{item.name}”</DialogTitle>
      <DialogContent dividers>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {toUserMessage(error)}
          </Alert>
        )}

        {canShare && (
          <Box component="form" onSubmit={submit} sx={{ mb: 2 }}>
            <Stack direction="row" spacing={1}>
              <TextField
                label="Thêm người theo email"
                type="email"
                size="small"
                fullWidth
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                slotProps={{ htmlInput: { maxLength: 256 } }}
              />
              <Select
                size="small"
                value={role}
                onChange={(e) => setRole(e.target.value as GrantRole)}
                sx={{ minWidth: 170 }}
              >
                {Object.entries(roleLabels).map(([value, label]) => (
                  <MenuItem key={value} value={value}>
                    {label}
                  </MenuItem>
                ))}
              </Select>
            </Stack>
            {email.trim() && (
              <Stack spacing={1} sx={{ mt: 1 }}>
                <FormControlLabel
                  control={<Checkbox checked={notify} onChange={(e) => setNotify(e.target.checked)} />}
                  label="Thông báo cho người này (email + thông báo trong MyCloud)"
                />
                {notify && (
                  <TextField
                    label="Lời nhắn (không bắt buộc)"
                    size="small"
                    multiline
                    minRows={2}
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                    slotProps={{ htmlInput: { maxLength: 1000 } }}
                  />
                )}
                <Button
                  type="submit"
                  variant="contained"
                  disabled={add.isPending}
                  sx={{ alignSelf: 'flex-end' }}
                >
                  Chia sẻ
                </Button>
              </Stack>
            )}
          </Box>
        )}

        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Những người có quyền truy cập
        </Typography>
        {sharing.isPending && <CircularProgress size={24} />}
        {data && (
          <List dense disablePadding>
            {data.owner && (
              <ListItem
                disableGutters
                secondaryAction={<Typography color="text.secondary">Chủ sở hữu</Typography>}
              >
                <ListItemAvatar>
                  <Avatar>{initials(data.owner.displayName)}</Avatar>
                </ListItemAvatar>
                <ListItemText
                  primary={`${data.owner.displayName}${data.owner.id === user?.id ? ' (bạn)' : ''}`}
                  secondary={data.owner.email}
                />
              </ListItem>
            )}
            {data.permissions.map((p) => (
              <ListItem
                key={p.id}
                disableGutters
                secondaryAction={
                  canShare && !p.inheritedFrom ? (
                    <Select
                      size="small"
                      variant="standard"
                      value={p.role}
                      disabled={change.isPending || revoke.isPending}
                      onChange={(e) => {
                        const value = e.target.value as GrantRole | 'remove';
                        if (value === 'remove') revoke.mutate(p.id);
                        else change.mutate({ id: p.id, role: value });
                      }}
                    >
                      {Object.entries(roleLabels).map(([value, label]) => (
                        <MenuItem key={value} value={value}>
                          {label}
                        </MenuItem>
                      ))}
                      <Divider />
                      <MenuItem value="remove">Xoá quyền truy cập</MenuItem>
                    </Select>
                  ) : (
                    <Typography color="text.secondary">{roleLabels[p.role]}</Typography>
                  )
                }
              >
                <ListItemAvatar>
                  <Avatar>{initials(p.user.displayName)}</Avatar>
                </ListItemAvatar>
                <ListItemText
                  primary={`${p.user.displayName}${p.user.id === user?.id ? ' (bạn)' : ''}`}
                  secondary={
                    p.inheritedFrom ? `${p.user.email} · kế thừa từ “${p.inheritedFrom.name}”` : p.user.email
                  }
                />
              </ListItem>
            ))}
          </List>
        )}

        {canShare && (
          <>
            <Divider sx={{ my: 2 }} />
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Quyền truy cập chung
            </Typography>
            {newLink && (
              <Alert
                severity="success"
                sx={{ mb: 1, wordBreak: 'break-all' }}
                action={
                  <Tooltip title={copied ? 'Đã sao chép' : 'Sao chép'}>
                    <IconButton
                      size="small"
                      onClick={() => void copy(newLink)}
                      aria-label="Sao chép đường liên kết"
                    >
                      <ContentCopyIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                }
              >
                Chỉ hiển thị một lần — hãy sao chép ngay: <b>{newLink}</b>
              </Alert>
            )}
            {(data?.links ?? []).map((l) => (
              <Stack key={l.id} direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                <LinkIcon color="action" />
                <Typography variant="body2" sx={{ flexGrow: 1 }}>
                  Bất kỳ ai có đường liên kết đều có thể xem
                  {l.hasPassword && ' (có mật khẩu)'}
                </Typography>
                {l.hasPassword && <LockOutlinedIcon fontSize="small" color="action" />}
                <Button
                  size="small"
                  color="error"
                  startIcon={<LinkOffIcon />}
                  onClick={() => removeLink.mutate(l.id)}
                >
                  Tắt
                </Button>
              </Stack>
            ))}
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <TextField
                size="small"
                type="password"
                label="Mật khẩu cho đường liên kết (không bắt buộc)"
                value={linkPassword}
                onChange={(e) => setLinkPassword(e.target.value)}
                sx={{ flexGrow: 1 }}
                slotProps={{ htmlInput: { maxLength: 128, autoComplete: 'new-password' } }}
              />
              <Button
                variant="outlined"
                startIcon={<LinkIcon />}
                disabled={makeLink.isPending || (linkPassword.length > 0 && linkPassword.length < 4)}
                onClick={() => makeLink.mutate()}
              >
                Tạo đường liên kết
              </Button>
            </Stack>
          </>
        )}
        {data && !canShare && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 2 }}>
            Chỉ chủ sở hữu và người chỉnh sửa mới thay đổi được quyền chia sẻ.
          </Typography>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Xong</Button>
      </DialogActions>
    </Dialog>
  );
}
