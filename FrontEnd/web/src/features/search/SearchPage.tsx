import SearchOffIcon from '@mui/icons-material/SearchOff';
import {
  Alert,
  Box,
  Button,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material';
import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import type { Category } from '../../api/search';
import { toUserMessage } from '../../api/errors';
import { ItemMenu, type MenuState } from '../drive/ItemMenu';
import { ItemTable } from '../drive/ItemViews';
import { useSearch } from '../drive/queries';
import { useSelection } from '../drive/selection';
import { useItemActions } from '../drive/useItemActions';
import { categoryLabels, hitToItem, modifiedAfter, modifiedLabels, type ModifiedRange } from './hits';

/** Kết quả tìm kiếm (Search service): theo tên (không dấu cũng được), lọc theo loại và thời gian sửa. */
export default function SearchPage() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const type = (params.get('type') as Category | null) ?? null;
  const range = (params.get('modified') as ModifiedRange | null) ?? 'any';

  // Mốc thời gian tính một lần cho mỗi bộ lọc (không đổi mỗi lần render ⇒ không tạo query mới liên tục).
  const filters = useMemo(() => ({ q, type, modifiedAfter: modifiedAfter(range) }), [q, type, range]);
  const enabled = q.trim().length > 0 || type !== null || range !== 'any';
  const results = useSearch(filters, enabled);
  const items = (results.data?.pages.flatMap((p) => p.items) ?? []).map((hit) => hitToItem(hit));
  const actions = useItemActions({ items });
  const selection = useSelection(items, JSON.stringify(filters));
  const [menu, setMenu] = useState<MenuState | null>(null);

  function setFilter(key: 'type' | 'modified', value: string) {
    const next = new URLSearchParams(params);
    if (value === '' || value === 'any') next.delete(key);
    else next.set(key, value);
    setParams(next);
  }

  return (
    <Box>
      <Typography variant="h6" component="h1" sx={{ mb: 2 }}>
        {q ? `Kết quả tìm kiếm cho “${q}”` : 'Tìm kiếm'}
      </Typography>

      <Stack direction="row" spacing={1.5} sx={{ mb: 2, flexWrap: 'wrap', rowGap: 1.5 }}>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="search-type">Loại</InputLabel>
          <Select
            labelId="search-type"
            label="Loại"
            value={type ?? ''}
            onChange={(e) => setFilter('type', e.target.value)}
          >
            <MenuItem value="">Tất cả</MenuItem>
            {Object.entries(categoryLabels).map(([value, label]) => (
              <MenuItem key={value} value={value}>
                {label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        <FormControl size="small" sx={{ minWidth: 160 }}>
          <InputLabel id="search-modified">Sửa đổi</InputLabel>
          <Select
            labelId="search-modified"
            label="Sửa đổi"
            value={range}
            onChange={(e) => setFilter('modified', e.target.value)}
          >
            {Object.entries(modifiedLabels).map(([value, label]) => (
              <MenuItem key={value} value={value}>
                {label}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Stack>

      {!enabled && (
        <Typography color="text.secondary">
          Nhập tên tệp hoặc thư mục vào ô tìm kiếm, hoặc chọn một bộ lọc.
        </Typography>
      )}
      {results.isError && <Alert severity="error">{toUserMessage(results.error)}</Alert>}
      {enabled && results.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}

      {results.isSuccess && items.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <SearchOffIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Không tìm thấy kết quả phù hợp
          </Typography>
          <Typography>
            Thử từ khoá khác hoặc bỏ bớt bộ lọc. Mục vừa tạo có thể mất vài giây mới tìm thấy.
          </Typography>
        </Stack>
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

      {results.hasNextPage && (
        <Box sx={{ textAlign: 'center', mt: 2 }}>
          <Button onClick={() => void results.fetchNextPage()} disabled={results.isFetchingNextPage}>
            Tải thêm
          </Button>
        </Box>
      )}

      <ItemMenu
        state={menu}
        targets={menu ? [menu.item] : []}
        actions={actions}
        mode="view"
        onClose={() => setMenu(null)}
      />
      {actions.elements}
    </Box>
  );
}
