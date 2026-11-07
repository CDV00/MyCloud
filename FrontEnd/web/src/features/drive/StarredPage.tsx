import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import StarIcon from '@mui/icons-material/Star';
import { Alert, Box, Button, Skeleton, Stack, Typography } from '@mui/material';
import { useState } from 'react';
import { toUserMessage } from '../../api/errors';
import { BarButton, ItemMenu, SelectionBar, type MenuState } from './ItemMenu';
import { ItemTable } from './ItemViews';
import { useStarred } from './queries';
import { useSelection } from './selection';
import { useItemActions } from './useItemActions';

/** "Có gắn dấu sao": item còn hoạt động mà người dùng đã gắn sao, gắn gần đây trước. */
export default function StarredPage() {
  const starred = useStarred();
  const actions = useItemActions();
  const items = starred.data?.pages.flatMap((p) => p.items) ?? [];
  const selection = useSelection(items);
  const { selectedItems } = selection;
  const [menu, setMenu] = useState<MenuState | null>(null);

  return (
    <Box>
      <Typography
        variant="h6"
        component="h1"
        sx={{ mb: 2, minHeight: 40, display: 'flex', alignItems: 'center' }}
      >
        Có gắn dấu sao
      </Typography>

      <SelectionBar
        count={selectedItems.length}
        onClear={selection.clear}
        hint="Nhấp đúp để mở · nhấp chuột phải để xem thao tác"
      >
        <BarButton
          title="Bỏ gắn dấu sao"
          onClick={() => {
            actions.star(selectedItems, false);
            selection.clear();
          }}
        >
          <StarIcon fontSize="small" />
        </BarButton>
        {selectedItems.length === 1 && (
          <BarButton title="Thông tin chi tiết" onClick={() => actions.details(selectedItems[0]!)}>
            <InfoOutlinedIcon fontSize="small" />
          </BarButton>
        )}
        <BarButton
          title="Chuyển vào thùng rác"
          onClick={() => {
            actions.trash(selectedItems);
            selection.clear();
          }}
        >
          <DeleteOutlineIcon fontSize="small" />
        </BarButton>
      </SelectionBar>

      {starred.isError && <Alert severity="error">{toUserMessage(starred.error)}</Alert>}
      {starred.isPending && [0, 1, 2].map((i) => <Skeleton key={i} height={44} />)}

      {starred.isSuccess && items.length === 0 && (
        <Stack
          spacing={1}
          sx={{ alignItems: 'center', textAlign: 'center', py: 10, color: 'text.secondary' }}
        >
          <StarBorderIcon sx={{ fontSize: 64 }} />
          <Typography variant="h6" color="text.primary">
            Chưa có mục nào được gắn dấu sao
          </Typography>
          <Typography>Gắn dấu sao cho tệp và thư mục quan trọng để tìm lại nhanh ở đây.</Typography>
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

      {starred.hasNextPage && (
        <Box sx={{ textAlign: 'center', mt: 2 }}>
          <Button onClick={() => void starred.fetchNextPage()} disabled={starred.isFetchingNextPage}>
            Tải thêm
          </Button>
        </Box>
      )}

      <ItemMenu
        state={menu}
        targets={menu ? selection.targetsFor(menu.item) : []}
        actions={actions}
        onClose={() => setMenu(null)}
      />
      {actions.elements}
    </Box>
  );
}
