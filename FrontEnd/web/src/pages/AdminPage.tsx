import AdminPanelSettingsOutlinedIcon from '@mui/icons-material/AdminPanelSettingsOutlined';
import BlockIcon from '@mui/icons-material/Block';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import SearchIcon from '@mui/icons-material/Search';
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  InputAdornment,
  LinearProgress,
  ListItemIcon,
  Menu,
  MenuItem,
  Paper,
  Skeleton,
  Snackbar,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useDeferredValue, useState } from 'react';
import { Navigate } from 'react-router';
import {
  getAdminSummary,
  getQuotas,
  getStorageSummary,
  listUsers,
  setQuota,
  updateUser,
  type AdminQuota,
  type AdminUser,
  type UserStatusFilter,
} from '../api/admin';
import { toUserMessage } from '../api/errors';
import { useAuth } from '../auth/authContext';
import { formatModified, formatSize } from '../lib/format';

const GB = 1024 ** 3;
const adminKeys = { all: ['admin'] as const };

function Stat({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, flex: '1 1 160px' }}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="h5" component="p">
        {value}
      </Typography>
      {hint && (
        <Typography variant="caption" color="text.secondary">
          {hint}
        </Typography>
      )}
    </Paper>
  );
}

function Usage({ quota }: { quota: AdminQuota | undefined }) {
  if (!quota) return <Skeleton width={120} />;
  const percent = quota.limitBytes > 0 ? Math.min(100, (quota.usedBytes / quota.limitBytes) * 100) : 100;
  return (
    <Box sx={{ minWidth: 140 }}>
      <LinearProgress
        variant="determinate"
        value={percent}
        color={percent >= 95 ? 'error' : percent >= 80 ? 'warning' : 'primary'}
        sx={{ height: 4, borderRadius: 2, mb: 0.5 }}
        aria-label={`Đã dùng ${percent.toFixed(0)}%`}
      />
      <Typography variant="caption" color="text.secondary">
        {formatSize(quota.usedBytes)} / {formatSize(quota.limitBytes)}
      </Typography>
    </Box>
  );
}

/**
 * Quản trị người dùng (chỉ quản trị viên): thống kê, tìm kiếm / lọc, dung lượng từng người, khoá / mở khoá tài khoản
 * (đăng xuất mọi thiết bị), cấp / thu quyền quản trị, đổi hạn mức dung lượng.
 */
export default function AdminPage() {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const q = useDeferredValue(search);
  const [status, setStatus] = useState<UserStatusFilter>('all');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [menu, setMenu] = useState<{ user: AdminUser; anchor: HTMLElement } | null>(null);
  const [confirm, setConfirm] = useState<{
    user: AdminUser;
    action: 'disable' | 'enable' | 'grant' | 'revoke';
  } | null>(null);
  const [quotaFor, setQuotaFor] = useState<{ user: AdminUser; gb: string } | null>(null);
  const [notice, setNotice] = useState<{ text: string; error?: boolean } | null>(null);

  const summary = useQuery({
    queryKey: [...adminKeys.all, 'summary'],
    queryFn: getAdminSummary,
    enabled: !!user?.isAdmin,
  });
  const storage = useQuery({
    queryKey: [...adminKeys.all, 'storage'],
    queryFn: getStorageSummary,
    enabled: !!user?.isAdmin,
  });
  const users = useQuery({
    queryKey: [...adminKeys.all, 'users', q, status, page, pageSize],
    queryFn: () => listUsers(q, status, page + 1, pageSize),
    placeholderData: keepPreviousData,
    enabled: !!user?.isAdmin,
  });
  const ids = users.data?.items.map((u) => u.id) ?? [];
  const quotas = useQuery({
    queryKey: [...adminKeys.all, 'quotas', ids],
    queryFn: () => getQuotas(ids),
    enabled: ids.length > 0,
  });
  const quotaOf = new Map((quotas.data ?? []).map((x) => [x.userId, x]));

  const refresh = () => void queryClient.invalidateQueries({ queryKey: adminKeys.all });
  const update = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: { isAdmin?: boolean; disabled?: boolean } }) =>
      updateUser(id, patch),
    onSuccess: (u) => setNotice({ text: `Đã cập nhật ${u.displayName}` }),
    onError: (e) => setNotice({ text: toUserMessage(e), error: true }),
    onSettled: refresh,
  });
  const quotaUpdate = useMutation({
    mutationFn: ({ id, bytes }: { id: string; bytes: number }) => setQuota(id, bytes),
    onSuccess: () => setNotice({ text: 'Đã đổi hạn mức dung lượng' }),
    onError: (e) => setNotice({ text: toUserMessage(e), error: true }),
    onSettled: refresh,
  });

  if (user && !user.isAdmin) return <Navigate to="/" replace />;

  const confirmText =
    confirm &&
    {
      disable: {
        title: `Khoá tài khoản ${confirm.user.displayName}?`,
        body: 'Người dùng bị đăng xuất khỏi mọi thiết bị và không đăng nhập được nữa. Tệp và quyền chia sẻ được giữ nguyên.',
        button: 'Khoá tài khoản',
      },
      enable: {
        title: `Mở khoá ${confirm.user.displayName}?`,
        body: 'Người dùng đăng nhập lại được.',
        button: 'Mở khoá',
      },
      grant: {
        title: `Cấp quyền quản trị cho ${confirm.user.displayName}?`,
        body: 'Người này sẽ xem và quản lý được mọi tài khoản. Quyền có hiệu lực ở lần đăng nhập / làm mới phiên kế tiếp.',
        button: 'Cấp quyền',
      },
      revoke: {
        title: `Thu quyền quản trị của ${confirm.user.displayName}?`,
        body: 'Có hiệu lực trong tối đa 15 phút (khi phiên hiện tại của họ hết hạn).',
        button: 'Thu quyền',
      },
    }[confirm.action];

  return (
    <Stack spacing={3}>
      <Typography variant="h5" component="h1">
        Quản trị người dùng
      </Typography>

      <Stack direction="row" useFlexGap spacing={2} sx={{ flexWrap: 'wrap' }}>
        <Stat
          label="Người dùng"
          value={summary.data?.users.toLocaleString('vi-VN') ?? '…'}
          hint={summary.data ? `${summary.data.activeLast30Days} hoạt động trong 30 ngày` : undefined}
        />
        <Stat label="Quản trị viên" value={summary.data?.admins.toLocaleString('vi-VN') ?? '…'} />
        <Stat label="Bị khoá" value={summary.data?.disabled.toLocaleString('vi-VN') ?? '…'} />
        <Stat
          label="Dung lượng đã dùng"
          value={storage.data ? formatSize(storage.data.usedBytes) : '…'}
          hint={storage.data ? `${storage.data.blobs.toLocaleString('vi-VN')} tệp / phiên bản` : undefined}
        />
      </Stack>
      {(summary.error || storage.error) && (
        <Alert severity="error">{toUserMessage(summary.error ?? storage.error)}</Alert>
      )}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: { sm: 'center' } }}>
        <TextField
          size="small"
          placeholder="Tìm theo tên hoặc email"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(0);
          }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
            htmlInput: { 'aria-label': 'Tìm người dùng', maxLength: 100 },
          }}
          sx={{ minWidth: 280 }}
        />
        <ToggleButtonGroup
          size="small"
          exclusive
          value={status}
          onChange={(_, v: UserStatusFilter | null) => {
            if (v) {
              setStatus(v);
              setPage(0);
            }
          }}
          aria-label="Lọc theo trạng thái"
        >
          <ToggleButton value="all">Tất cả</ToggleButton>
          <ToggleButton value="active">Đang hoạt động</ToggleButton>
          <ToggleButton value="disabled">Bị khoá</ToggleButton>
          <ToggleButton value="admin">Quản trị viên</ToggleButton>
        </ToggleButtonGroup>
      </Stack>

      {users.error && <Alert severity="error">{toUserMessage(users.error)}</Alert>}
      <Paper variant="outlined" sx={{ borderRadius: 3, overflow: 'auto' }}>
        {users.isFetching && <LinearProgress sx={{ height: 2 }} />}
        <Table size="small" aria-label="Người dùng">
          <TableHead>
            <TableRow>
              <TableCell>Người dùng</TableCell>
              <TableCell>Trạng thái</TableCell>
              <TableCell>Dung lượng</TableCell>
              <TableCell sx={{ display: { xs: 'none', md: 'table-cell' } }}>Đăng nhập gần nhất</TableCell>
              <TableCell sx={{ display: { xs: 'none', lg: 'table-cell' } }}>Ngày tạo</TableCell>
              <TableCell padding="checkbox" />
            </TableRow>
          </TableHead>
          <TableBody>
            {users.isPending &&
              [0, 1, 2].map((i) => (
                <TableRow key={i}>
                  <TableCell colSpan={6}>
                    <Skeleton />
                  </TableCell>
                </TableRow>
              ))}
            {users.data?.items.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} sx={{ textAlign: 'center', py: 4, color: 'text.secondary' }}>
                  Không có người dùng phù hợp
                </TableCell>
              </TableRow>
            )}
            {users.data?.items.map((u) => (
              <TableRow key={u.id} hover>
                <TableCell>
                  <Typography variant="body2" sx={{ fontWeight: 500 }}>
                    {u.displayName}
                    {u.id === user?.id && (
                      <Typography component="span" variant="body2" color="text.secondary">
                        {' '}
                        (bạn)
                      </Typography>
                    )}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {u.email}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Stack direction="row" spacing={0.5}>
                    {u.disabled ? (
                      <Chip
                        size="small"
                        color="error"
                        variant="outlined"
                        icon={<BlockIcon />}
                        label="Bị khoá"
                      />
                    ) : (
                      <Chip size="small" color="success" variant="outlined" label="Hoạt động" />
                    )}
                    {u.isAdmin && (
                      <Chip
                        size="small"
                        color="primary"
                        variant="outlined"
                        icon={<AdminPanelSettingsOutlinedIcon />}
                        label="Quản trị"
                      />
                    )}
                  </Stack>
                </TableCell>
                <TableCell>
                  <Usage quota={quotaOf.get(u.id)} />
                </TableCell>
                <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, color: 'text.secondary' }}>
                  {u.lastLoginAt ? formatModified(u.lastLoginAt) : 'Chưa đăng nhập'}
                </TableCell>
                <TableCell sx={{ display: { xs: 'none', lg: 'table-cell' }, color: 'text.secondary' }}>
                  {formatModified(u.createdAt)}
                </TableCell>
                <TableCell padding="checkbox">
                  <IconButton
                    size="small"
                    aria-label={`Thao tác với ${u.displayName}`}
                    onClick={(e) => setMenu({ user: u, anchor: e.currentTarget })}
                  >
                    <MoreVertIcon fontSize="small" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <TablePagination
          component="div"
          count={users.data?.total ?? 0}
          page={page}
          onPageChange={(_, p) => setPage(p)}
          rowsPerPage={pageSize}
          onRowsPerPageChange={(e) => {
            setPageSize(Number(e.target.value));
            setPage(0);
          }}
          rowsPerPageOptions={[25, 50, 100]}
          labelRowsPerPage="Số dòng"
          labelDisplayedRows={({ from, to, count }) => `${from}–${to} / ${count}`}
        />
      </Paper>

      <Menu anchorEl={menu?.anchor} open={menu !== null} onClose={() => setMenu(null)}>
        {menu && [
          <MenuItem
            key="quota"
            onClick={() => {
              const current = quotaOf.get(menu.user.id)?.limitBytes ?? 15 * GB;
              setQuotaFor({ user: menu.user, gb: String(+(current / GB).toFixed(2)) });
              setMenu(null);
            }}
          >
            Đổi hạn mức dung lượng
          </MenuItem>,
          <MenuItem
            key="admin"
            disabled={menu.user.id === user?.id}
            onClick={() => {
              setConfirm({ user: menu.user, action: menu.user.isAdmin ? 'revoke' : 'grant' });
              setMenu(null);
            }}
          >
            <ListItemIcon>
              <AdminPanelSettingsOutlinedIcon fontSize="small" />
            </ListItemIcon>
            {menu.user.isAdmin ? 'Thu quyền quản trị' : 'Cấp quyền quản trị'}
          </MenuItem>,
          <MenuItem
            key="disable"
            disabled={menu.user.id === user?.id}
            onClick={() => {
              setConfirm({ user: menu.user, action: menu.user.disabled ? 'enable' : 'disable' });
              setMenu(null);
            }}
            sx={{ color: menu.user.disabled ? undefined : 'error.main' }}
          >
            <ListItemIcon sx={{ color: 'inherit' }}>
              <BlockIcon fontSize="small" />
            </ListItemIcon>
            {menu.user.disabled ? 'Mở khoá tài khoản' : 'Khoá tài khoản'}
          </MenuItem>,
        ]}
      </Menu>

      <Dialog open={confirm !== null} onClose={() => setConfirm(null)} maxWidth="xs">
        <DialogTitle>{confirmText?.title}</DialogTitle>
        <DialogContent>
          <DialogContentText>{confirmText?.body}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(null)}>Huỷ</Button>
          <Button
            variant="contained"
            color={confirm?.action === 'disable' ? 'error' : 'primary'}
            onClick={() => {
              if (!confirm) return;
              const patch =
                confirm.action === 'disable' || confirm.action === 'enable'
                  ? { disabled: confirm.action === 'disable' }
                  : { isAdmin: confirm.action === 'grant' };
              update.mutate({ id: confirm.user.id, patch });
              setConfirm(null);
            }}
          >
            {confirmText?.button}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={quotaFor !== null} onClose={() => setQuotaFor(null)} maxWidth="xs" fullWidth>
        <DialogTitle>Hạn mức dung lượng của {quotaFor?.user.displayName}</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            fullWidth
            margin="dense"
            type="number"
            label="Hạn mức"
            value={quotaFor?.gb ?? ''}
            onChange={(e) => setQuotaFor((x) => (x ? { ...x, gb: e.target.value } : x))}
            helperText={
              quotaFor && quotaOf.get(quotaFor.user.id)
                ? `Đang dùng ${formatSize(quotaOf.get(quotaFor.user.id)!.usedBytes)}. Hạn mức thấp hơn mức đang dùng: tệp cũ giữ nguyên, không tải lên thêm được.`
                : undefined
            }
            slotProps={{
              input: { endAdornment: <InputAdornment position="end">GB</InputAdornment> },
              htmlInput: { min: 0, step: 1 },
            }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setQuotaFor(null)}>Huỷ</Button>
          <Button
            variant="contained"
            disabled={!quotaFor || !(Number(quotaFor.gb) >= 0) || quotaFor.gb.trim() === ''}
            onClick={() => {
              if (!quotaFor) return;
              quotaUpdate.mutate({ id: quotaFor.user.id, bytes: Math.round(Number(quotaFor.gb) * GB) });
              setQuotaFor(null);
            }}
          >
            Lưu
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={notice !== null} autoHideDuration={4000} onClose={() => setNotice(null)}>
        <Alert severity={notice?.error ? 'error' : 'success'} onClose={() => setNotice(null)}>
          {notice?.text}
        </Alert>
      </Snackbar>
    </Stack>
  );
}
