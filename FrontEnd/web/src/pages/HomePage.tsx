import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import { Box, Button, Card, CardActionArea, Skeleton, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { useAuth } from '../auth/authContext';
import { FileIcon } from '../features/drive/FileIcon';
import { useRecent } from '../features/drive/queries';
import { useItemActions } from '../features/drive/useItemActions';
import { hitToItem } from '../features/search/hits';
import { formatModified } from '../lib/format';

/** Trang chủ: lời chào + tệp dùng gần đây (Search service). */
export default function HomePage() {
  const { user } = useAuth();
  const recent = useRecent();
  const items = (recent.data ?? []).slice(0, 8);
  const actions = useItemActions({ items: items.map((r) => hitToItem(r.item)) });

  return (
    <Stack spacing={3}>
      <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
        <Typography variant="h5" component="h1">
          Chào mừng {user?.displayName} đến với MyCloud
        </Typography>
        <Button component={RouterLink} to="/my-drive" variant="outlined" startIcon={<CloudQueueIcon />}>
          Mở Drive của tôi
        </Button>
      </Stack>

      <Box>
        <Stack direction="row" sx={{ alignItems: 'center', mb: 1.5 }}>
          <Typography variant="subtitle1" component="h2" sx={{ flexGrow: 1, fontWeight: 600 }}>
            Tệp gần đây
          </Typography>
          {items.length > 0 && (
            <Button component={RouterLink} to="/recent" size="small">
              Xem tất cả
            </Button>
          )}
        </Stack>
        {recent.isPending && <Skeleton variant="rounded" height={88} />}
        {recent.isSuccess && items.length === 0 && (
          <Typography color="text.secondary">Tệp bạn mở hoặc tải lên sẽ hiện ở đây.</Typography>
        )}
        <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))' }}>
          {items.map(({ item: hit, lastAccessAt }) => {
            const item = hitToItem(hit);
            return (
              <Card key={hit.id} variant="outlined" sx={{ borderRadius: 3, bgcolor: 'background.default' }}>
                <CardActionArea onClick={() => actions.open(item)} sx={{ p: 1.5 }} aria-label={hit.name}>
                  <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', minWidth: 0 }}>
                    <FileIcon item={item} />
                    <Box sx={{ minWidth: 0 }}>
                      <Typography noWrap variant="body2" sx={{ fontWeight: 500 }} title={hit.name}>
                        {hit.name}
                      </Typography>
                      <Typography variant="caption" color="text.secondary">
                        {formatModified(lastAccessAt)}
                      </Typography>
                    </Box>
                  </Stack>
                </CardActionArea>
              </Card>
            );
          })}
        </Box>
      </Box>
      {actions.elements}
    </Stack>
  );
}
