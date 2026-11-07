import { Box, LinearProgress, Typography } from '@mui/material';
import { formatSize } from '../../lib/format';
import { useQuota } from './queries';

/** "Đã dùng 1,2 GB trong tổng số 15 GB" — ở thanh bên và trang Bộ nhớ. */
export function QuotaBar() {
  const quota = useQuota();
  if (!quota.data) return null;

  const { usedBytes, limitBytes } = quota.data;
  const percent = limitBytes > 0 ? Math.min(100, (usedBytes / limitBytes) * 100) : 100;

  return (
    <Box sx={{ px: 2, py: 1 }}>
      <LinearProgress
        variant="determinate"
        value={percent}
        color={percent >= 95 ? 'error' : percent >= 80 ? 'warning' : 'primary'}
        sx={{ height: 4, borderRadius: 2, mb: 1 }}
        aria-label="Dung lượng đã dùng"
      />
      <Typography variant="caption" color="text.secondary">
        Đã dùng {formatSize(usedBytes)} trong tổng số {formatSize(limitBytes)}
      </Typography>
    </Box>
  );
}
