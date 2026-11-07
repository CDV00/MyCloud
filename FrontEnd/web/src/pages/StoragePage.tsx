import {
  Alert,
  Box,
  LinearProgress,
  Paper,
  Skeleton,
  Stack,
  Tooltip,
  Typography,
  useTheme,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { toUserMessage } from '../api/errors';
import { getLargestFiles, getUsage, type Category } from '../api/search';
import { ItemMenu, type MenuState } from '../features/drive/ItemMenu';
import { ItemTable } from '../features/drive/ItemViews';
import { searchKeys, useQuota } from '../features/drive/queries';
import { useSelection } from '../features/drive/selection';
import { useItemActions } from '../features/drive/useItemActions';
import { categoryLabels, hitToItem } from '../features/search/hits';
import { formatSize } from '../lib/format';

// Mỗi loại một màu cố định (màu đi theo loại, không theo thứ hạng) — bảng màu phân loại đã kiểm tra mù màu.
const CATEGORY_COLORS: Partial<Record<Category, { light: string; dark: string }>> = {
  image: { light: '#2a78d6', dark: '#3987e5' },
  video: { light: '#eb6834', dark: '#d95926' },
  doc: { light: '#1baf7a', dark: '#199e70' },
  audio: { light: '#eda100', dark: '#c98500' },
  pdf: { light: '#e87ba4', dark: '#d55181' },
  sheet: { light: '#008300', dark: '#008300' },
  slide: { light: '#6250d6', dark: '#9085e9' },
  archive: { light: '#e34948', dark: '#e66767' },
};

interface Segment {
  key: string;
  label: string;
  bytes: number;
  count?: number;
  color: string;
}

/**
 * Bộ nhớ (Phase 5): dung lượng đã dùng / còn trống (Storage), chia theo loại file và thùng rác (Search), phần còn lại
 * là phiên bản cũ + tải lên dở; danh sách file lớn nhất để dọn dẹp.
 */
export default function StoragePage() {
  const theme = useTheme();
  const dark = theme.palette.mode === 'dark';
  const quota = useQuota();
  const usage = useQuery({ queryKey: [...searchKeys.all, 'usage'], queryFn: getUsage });
  const largest = useQuery({ queryKey: [...searchKeys.all, 'largest'], queryFn: () => getLargestFiles(50) });
  const items = (largest.data ?? []).map((hit) => hitToItem(hit, 'owner'));
  const actions = useItemActions({
    items,
    onChanged: () => void usage.refetch().then(() => largest.refetch()),
  });
  const selection = useSelection(items);
  const [menu, setMenu] = useState<MenuState | null>(null);

  if (quota.error) return <Alert severity="error">{toUserMessage(quota.error)}</Alert>;
  if (!quota.data) return <LinearProgress />;

  const { usedBytes, limitBytes, reservedBytes, availableBytes } = quota.data;
  const percent = limitBytes > 0 ? Math.min(100, (usedBytes / limitBytes) * 100) : 100;
  const neutral = dark ? '#8a8986' : '#9e9d98';
  const neutralLight = dark ? '#5c5b57' : '#cfcec8';

  // Tối đa 8 loại có màu; "Khác" và phần chưa phân loại dùng màu trung tính.
  const segments: Segment[] = [];
  let other = 0;
  let otherCount = 0;
  for (const c of usage.data?.categories ?? []) {
    const color = CATEGORY_COLORS[c.category as Category];
    if (color && c.bytes > 0) {
      segments.push({
        key: c.category,
        label: categoryLabels[c.category as Category],
        bytes: c.bytes,
        count: c.count,
        color: dark ? color.dark : color.light,
      });
    } else {
      other += c.bytes;
      otherCount += c.count;
    }
  }
  if (other > 0)
    segments.push({ key: 'other', label: 'Khác', bytes: other, count: otherCount, color: neutral });
  const trash = usage.data?.trash.bytes ?? 0;
  if (trash > 0)
    segments.push({
      key: 'trash',
      label: 'Thùng rác',
      bytes: trash,
      count: usage.data?.trash.count,
      color: neutralLight,
    });
  const classified = segments.reduce((sum, s) => sum + s.bytes, 0);
  const versions = Math.max(0, usedBytes - classified);
  if (usage.data && versions > 0) {
    segments.push({
      key: 'versions',
      label: 'Phiên bản cũ và khác',
      bytes: versions,
      color: dark ? '#3e3d3a' : '#e4e3dd',
    });
  }

  return (
    <Stack spacing={3} sx={{ maxWidth: 960 }}>
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

        {usage.isPending ? (
          <Skeleton variant="rounded" height={12} />
        ) : (
          <Box
            role="img"
            aria-label={`Dung lượng đã dùng theo loại: ${segments.map((s) => `${s.label} ${formatSize(s.bytes)}`).join(', ')}`}
            sx={{
              display: 'flex',
              gap: '2px',
              height: 12,
              borderRadius: '4px',
              overflow: 'hidden',
              bgcolor: 'action.hover',
            }}
          >
            {limitBytes > 0 &&
              segments.map((s) => (
                <Tooltip key={s.key} title={`${s.label}: ${formatSize(s.bytes)}`} arrow>
                  <Box
                    sx={{
                      flex: `0 0 ${Math.max(0.4, (s.bytes / limitBytes) * 100)}%`,
                      bgcolor: s.color,
                      cursor: 'default',
                    }}
                  />
                </Tooltip>
              ))}
          </Box>
        )}

        {usage.error && (
          <Alert severity="warning" sx={{ mt: 2 }}>
            Chưa xem được dung lượng theo loại: {toUserMessage(usage.error)}
          </Alert>
        )}

        <Box
          component="ul"
          aria-label="Chi tiết dung lượng"
          sx={{
            listStyle: 'none',
            p: 0,
            m: 0,
            mt: 2.5,
            display: 'grid',
            gap: 1,
            gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
          }}
        >
          {segments.map((s) => (
            <Box component="li" key={s.key} sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: s.color, flexShrink: 0 }} />
              <Typography variant="body2" sx={{ flexGrow: 1 }}>
                {s.label}
                {s.count !== undefined && (
                  <Typography component="span" variant="body2" color="text.secondary">
                    {' '}
                    · {s.count.toLocaleString('vi-VN')} tệp
                  </Typography>
                )}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {formatSize(s.bytes)}
              </Typography>
            </Box>
          ))}
          <Box component="li" sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Box
              sx={{
                width: 10,
                height: 10,
                borderRadius: '50%',
                border: 1,
                borderColor: 'divider',
                flexShrink: 0,
              }}
            />
            <Typography variant="body2" sx={{ flexGrow: 1 }}>
              Còn trống
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {formatSize(availableBytes)}
            </Typography>
          </Box>
        </Box>
        {reservedBytes > 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
            Đang tải lên: {formatSize(reservedBytes)}
          </Typography>
        )}
      </Paper>

      <Box>
        <Typography variant="subtitle1" component="h2" sx={{ fontWeight: 600, mb: 1 }}>
          Tệp lớn nhất
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          Xoá tệp không cần nữa rồi dọn thùng rác để giải phóng dung lượng. Danh sách cập nhật sau vài giây.
        </Typography>
        {largest.error && <Alert severity="error">{toUserMessage(largest.error)}</Alert>}
        {largest.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}
        {largest.data && largest.data.length === 0 && (
          <Typography color="text.secondary">Bạn chưa có tệp nào.</Typography>
        )}
        {items.length > 0 && (
          <ItemTable
            items={items}
            selected={selection.selected}
            onToggle={selection.toggle}
            onToggleAll={selection.toggleAll}
            onOpen={actions.open}
            onMenu={(item, anchor) => setMenu({ item, anchor })}
          />
        )}
      </Box>

      <ItemMenu
        state={menu}
        targets={menu ? selection.targetsFor(menu.item) : []}
        actions={actions}
        mode="drive"
        onClose={() => setMenu(null)}
      />
      {actions.elements}
    </Stack>
  );
}
