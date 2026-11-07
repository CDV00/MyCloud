import { Alert, LinearProgress, Paper, Stack, Typography } from '@mui/material';
import { toUserMessage } from '../api/errors';
import { useQuota } from '../features/drive/queries';
import { formatSize } from '../lib/format';

/** Bộ nhớ: dung lượng đã dùng / còn trống. Phân loại theo loại file ở Phase 3 (từ Search). */
export default function StoragePage() {
  const quota = useQuota();

  if (quota.error) return <Alert severity="error">{toUserMessage(quota.error)}</Alert>;
  if (!quota.data) return <LinearProgress />;

  const { usedBytes, limitBytes, reservedBytes, availableBytes } = quota.data;
  const percent = limitBytes > 0 ? Math.min(100, (usedBytes / limitBytes) * 100) : 100;

  return (
    <Stack spacing={3} sx={{ maxWidth: 640 }}>
      <Typography variant="h5" component="h1">
        Bộ nhớ
      </Typography>
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
        <Typography variant="h4" component="p">
          {formatSize(usedBytes)}
        </Typography>
        <Typography color="text.secondary" sx={{ mb: 2 }}>
          đã dùng trong tổng số {formatSize(limitBytes)} (
          {percent.toLocaleString('vi-VN', { maximumFractionDigits: 1 })}%)
        </Typography>
        <LinearProgress
          variant="determinate"
          value={percent}
          sx={{ height: 8, borderRadius: 4 }}
          aria-label="Dung lượng đã dùng"
        />
        <Stack direction="row" spacing={4} sx={{ mt: 2 }}>
          <Typography variant="body2">Còn trống: {formatSize(availableBytes)}</Typography>
          {reservedBytes > 0 && (
            <Typography variant="body2" color="text.secondary">
              Đang tải lên: {formatSize(reservedBytes)}
            </Typography>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}
