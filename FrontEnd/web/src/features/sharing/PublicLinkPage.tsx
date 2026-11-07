import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import CloudIcon from '@mui/icons-material/Cloud';
import DownloadIcon from '@mui/icons-material/Download';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import {
  Alert,
  AppBar,
  Box,
  Breadcrumbs,
  Button,
  Card,
  CardContent,
  CircularProgress,
  IconButton,
  Link,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { Link as RouterLink, useParams, useSearchParams } from 'react-router';
import {
  getLinkedAncestors,
  getLinkedDownloadUrl,
  getLinkedItem,
  listLinkedChildren,
  openLink,
  type DriveItem,
} from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { ApiError } from '../../api/http';
import { formatModified, formatSize } from '../../lib/format';
import { FileIcon } from '../drive/FileIcon';

/**
 * Trang mở link chia sẻ <c>/s/:token</c> — không cần đăng nhập. Duyệt thư mục con và tải file trong phạm vi link;
 * mật khẩu (nếu có) chỉ giữ trong bộ nhớ của tab và gửi trong header, không đặt trên URL.
 */
export default function PublicLinkPage() {
  const { token = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const [password, setPassword] = useState<string | null>(null);
  const [typed, setTyped] = useState('');
  const [error, setError] = useState<string | null>(null);

  const root = useQuery({
    queryKey: ['link', token, password],
    queryFn: () => openLink(token, password),
    retry: false,
  });
  const needsPassword =
    root.error instanceof ApiError && root.error.problem.code === 'DRIVE.LINK_PASSWORD_REQUIRED';
  const folderId = params.get('folder') ?? (root.data?.kind === 'folder' ? root.data.id : undefined);

  const folder = useQuery({
    queryKey: ['link', token, password, 'item', folderId],
    queryFn: () => getLinkedItem(token, folderId!, password),
    enabled: !!root.data && !!folderId && folderId !== root.data.id,
  });
  const children = useQuery({
    queryKey: ['link', token, password, 'children', folderId],
    queryFn: () => listLinkedChildren(token, folderId!, password),
    enabled: !!root.data && !!folderId,
  });
  const ancestors = useQuery({
    queryKey: ['link', token, password, 'ancestors', folderId],
    queryFn: () => getLinkedAncestors(token, folderId!, password),
    enabled: !!root.data && !!folderId && folderId !== root.data.id,
  });

  async function download(item: DriveItem) {
    try {
      const { url } = await getLinkedDownloadUrl(token, item.id, password);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.rel = 'noopener';
      document.body.append(anchor);
      anchor.click();
      anchor.remove();
    } catch (e) {
      setError(toUserMessage(e));
    }
  }

  function openFolder(id: string) {
    const next = new URLSearchParams(params);
    if (id === root.data?.id) next.delete('folder');
    else next.set('folder', id);
    setParams(next);
  }

  function submitPassword(event: FormEvent) {
    event.preventDefault();
    setPassword(typed);
  }

  const current = folderId === root.data?.id ? root.data : folder.data;

  return (
    <Box sx={{ minHeight: '100vh', bgcolor: 'background.default' }}>
      <AppBar position="static" color="inherit" elevation={0}>
        <Toolbar>
          <CloudIcon color="primary" sx={{ mr: 1 }} />
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            MyCloud
          </Typography>
          <Button component={RouterLink} to="/login">
            Đăng nhập
          </Button>
        </Toolbar>
      </AppBar>

      <Box sx={{ maxWidth: 960, mx: 'auto', p: { xs: 2, md: 3 } }}>
        {root.isPending && <CircularProgress />}

        {needsPassword && (
          <Card variant="outlined" sx={{ maxWidth: 420, mx: 'auto', mt: 6 }}>
            <CardContent component="form" onSubmit={submitPassword}>
              <Stack spacing={2} sx={{ alignItems: 'center' }}>
                <LockOutlinedIcon color="action" sx={{ fontSize: 48 }} />
                <Typography variant="h6">Đường liên kết này cần mật khẩu</Typography>
                {password !== null && <Alert severity="error">Mật khẩu không đúng.</Alert>}
                <TextField
                  type="password"
                  label="Mật khẩu"
                  fullWidth
                  autoFocus
                  value={typed}
                  onChange={(e) => setTyped(e.target.value)}
                />
                <Button type="submit" variant="contained" disabled={!typed}>
                  Mở
                </Button>
              </Stack>
            </CardContent>
          </Card>
        )}

        {root.isError && !needsPassword && (
          <Stack
            spacing={1}
            sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
          >
            <LinkOffIcon sx={{ fontSize: 64 }} />
            <Typography variant="h6" color="text.primary">
              {toUserMessage(root.error)}
            </Typography>
          </Stack>
        )}

        {error && (
          <Alert severity="error" onClose={() => setError(null)} sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}

        {root.data?.kind === 'file' && (
          <Card variant="outlined" sx={{ maxWidth: 520, mx: 'auto', mt: 6 }}>
            <CardContent>
              <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center' }}>
                <FileIcon item={root.data} sx={{ fontSize: 72 }} />
                <Typography variant="h6" sx={{ wordBreak: 'break-word' }}>
                  {root.data.name}
                </Typography>
                <Typography color="text.secondary">
                  {formatSize(root.data.sizeBytes)} · sửa đổi {formatModified(root.data.modifiedAt)}
                </Typography>
                <Button
                  variant="contained"
                  startIcon={<DownloadIcon />}
                  onClick={() => void download(root.data!)}
                >
                  Tải xuống
                </Button>
              </Stack>
            </CardContent>
          </Card>
        )}

        {root.data?.kind === 'folder' && (
          <>
            <Breadcrumbs separator={<ChevronRightIcon fontSize="small" />} sx={{ mb: 2 }}>
              {(ancestors.data ?? []).map((a) => (
                <Link
                  key={a.id}
                  component="button"
                  underline="hover"
                  color="inherit"
                  variant="h6"
                  onClick={() => openFolder(a.id)}
                >
                  {a.name}
                </Link>
              ))}
              <Typography variant="h6" component="h1">
                {current?.name ?? '…'}
              </Typography>
            </Breadcrumbs>
            {children.isError && <Alert severity="error">{toUserMessage(children.error)}</Alert>}
            {children.isPending && <CircularProgress size={28} />}
            {children.data && children.data.items.length === 0 && (
              <Typography color="text.secondary">Thư mục trống.</Typography>
            )}
            {children.data && children.data.items.length > 0 && (
              <Table size="small" sx={{ tableLayout: 'fixed' }}>
                <TableHead>
                  <TableRow>
                    <TableCell>Tên</TableCell>
                    <TableCell sx={{ width: 160, display: { xs: 'none', sm: 'table-cell' } }}>
                      Sửa đổi lần cuối
                    </TableCell>
                    <TableCell sx={{ width: 120, display: { xs: 'none', md: 'table-cell' } }}>
                      Kích thước
                    </TableCell>
                    <TableCell padding="checkbox" />
                  </TableRow>
                </TableHead>
                <TableBody>
                  {children.data.items.map((item) => (
                    <TableRow
                      key={item.id}
                      hover
                      onDoubleClick={() =>
                        item.kind === 'folder' ? openFolder(item.id) : void download(item)
                      }
                      sx={{ cursor: item.kind === 'folder' ? 'pointer' : 'default' }}
                    >
                      <TableCell>
                        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
                          <FileIcon item={item} fontSize="small" />
                          {item.kind === 'folder' ? (
                            <Link
                              component="button"
                              underline="hover"
                              color="inherit"
                              onClick={() => openFolder(item.id)}
                              noWrap
                            >
                              {item.name}
                            </Link>
                          ) : (
                            <Typography noWrap variant="body2" title={item.name}>
                              {item.name}
                            </Typography>
                          )}
                        </Box>
                      </TableCell>
                      <TableCell sx={{ display: { xs: 'none', sm: 'table-cell' }, color: 'text.secondary' }}>
                        {formatModified(item.modifiedAt)}
                      </TableCell>
                      <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, color: 'text.secondary' }}>
                        {item.kind === 'file' ? formatSize(item.sizeBytes) : '—'}
                      </TableCell>
                      <TableCell padding="checkbox">
                        {item.kind === 'file' && (
                          <Tooltip title="Tải xuống">
                            <IconButton
                              size="small"
                              onClick={() => void download(item)}
                              aria-label={`Tải xuống ${item.name}`}
                            >
                              <DownloadIcon fontSize="small" />
                            </IconButton>
                          </Tooltip>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </>
        )}
      </Box>
    </Box>
  );
}
