import MoreVertIcon from '@mui/icons-material/MoreVert';
import StarIcon from '@mui/icons-material/Star';
import {
  Box,
  Card,
  CardActionArea,
  Checkbox,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import type { MouseEvent } from 'react';
import type { DriveItem } from '../../api/drive';
import { formatModified, formatSize } from '../../lib/format';
import { FileIcon } from './FileIcon';

export interface ItemViewProps {
  items: DriveItem[];
  selected: ReadonlySet<string>;
  onToggle(item: DriveItem, additive: boolean): void;
  onToggleAll(): void;
  onOpen(item: DriveItem): void;
  onMenu(item: DriveItem, anchor: HTMLElement | { top: number; left: number }): void;
  /** Cột ngày: mặc định "Sửa đổi lần cuối"; thùng rác dùng "Ngày xoá", Gần đây dùng "Lần mở gần nhất". */
  dateColumn?: { label: string; value(item: DriveItem): string | null };
}

const modifiedColumn = { label: 'Sửa đổi lần cuối', value: (item: DriveItem) => item.modifiedAt };

function StarMark({ item }: { item: DriveItem }) {
  return item.starred ? (
    <StarIcon sx={{ fontSize: 16, color: 'text.secondary', flexShrink: 0 }} aria-label="Có gắn dấu sao" />
  ) : null;
}

function contextMenu(props: ItemViewProps, item: DriveItem) {
  return (e: MouseEvent) => {
    e.preventDefault();
    if (!props.selected.has(item.id)) props.onToggle(item, false);
    props.onMenu(item, { top: e.clientY, left: e.clientX });
  };
}

/** Dạng danh sách: tên, sửa đổi lần cuối, kích thước. Nhấp đúp để mở thư mục / tải file. */
export function ItemTable(props: ItemViewProps) {
  const { items, selected } = props;
  const dateColumn = props.dateColumn ?? modifiedColumn;
  const allSelected = items.length > 0 && items.every((i) => selected.has(i.id));

  return (
    <Table size="small" aria-label="Nội dung thư mục" sx={{ tableLayout: 'fixed' }}>
      <TableHead>
        <TableRow>
          <TableCell padding="checkbox">
            <Checkbox
              size="small"
              checked={allSelected}
              indeterminate={!allSelected && selected.size > 0}
              onChange={props.onToggleAll}
              slotProps={{ input: { 'aria-label': 'Chọn tất cả' } }}
            />
          </TableCell>
          <TableCell>Tên</TableCell>
          <TableCell sx={{ width: 160, display: { xs: 'none', sm: 'table-cell' } }}>
            {dateColumn.label}
          </TableCell>
          <TableCell sx={{ width: 120, display: { xs: 'none', md: 'table-cell' } }}>Kích thước</TableCell>
          <TableCell padding="checkbox" />
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((item) => {
          const isSelected = selected.has(item.id);
          return (
            <TableRow
              key={item.id}
              hover
              selected={isSelected}
              onClick={(e) => props.onToggle(item, e.ctrlKey || e.metaKey)}
              onDoubleClick={() => props.onOpen(item)}
              onContextMenu={contextMenu(props, item)}
              sx={{ cursor: 'default', userSelect: 'none' }}
            >
              <TableCell padding="checkbox" onClick={(e) => e.stopPropagation()}>
                <Checkbox
                  size="small"
                  checked={isSelected}
                  onChange={() => props.onToggle(item, true)}
                  slotProps={{ input: { 'aria-label': `Chọn ${item.name}` } }}
                />
              </TableCell>
              <TableCell>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
                  <FileIcon item={item} fontSize="small" />
                  <Typography noWrap title={item.name} variant="body2">
                    {item.name}
                  </Typography>
                  <StarMark item={item} />
                </Box>
              </TableCell>
              <TableCell sx={{ display: { xs: 'none', sm: 'table-cell' }, color: 'text.secondary' }}>
                {(() => {
                  const value = dateColumn.value(item);
                  return value ? formatModified(value) : '—';
                })()}
              </TableCell>
              <TableCell sx={{ display: { xs: 'none', md: 'table-cell' }, color: 'text.secondary' }}>
                {item.kind === 'file' ? formatSize(item.sizeBytes) : '—'}
              </TableCell>
              <TableCell padding="checkbox" onClick={(e) => e.stopPropagation()}>
                <IconButton
                  size="small"
                  aria-label={`Thao tác với ${item.name}`}
                  onClick={(e) => props.onMenu(item, e.currentTarget)}
                >
                  <MoreVertIcon fontSize="small" />
                </IconButton>
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

/** Dạng lưới: thư mục thành dải gọn ở trên, file thành thẻ lớn. */
export function ItemGrid(props: ItemViewProps) {
  const folders = props.items.filter((i) => i.kind === 'folder');
  const files = props.items.filter((i) => i.kind === 'file');

  const card = (item: DriveItem, big: boolean) => {
    const isSelected = props.selected.has(item.id);
    return (
      <Card
        key={item.id}
        variant="outlined"
        onContextMenu={contextMenu(props, item)}
        sx={{
          borderRadius: 3,
          bgcolor: isSelected ? 'primary.light' : 'background.default',
          borderColor: isSelected ? 'primary.main' : 'divider',
        }}
      >
        <CardActionArea
          onClick={(e) => props.onToggle(item, e.ctrlKey || e.metaKey)}
          onDoubleClick={() => props.onOpen(item)}
          aria-label={item.name}
          sx={{ p: 1.5 }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, minWidth: 0 }}>
            <FileIcon item={item} fontSize="small" />
            <Typography noWrap variant="body2" sx={{ fontWeight: 500 }} title={item.name}>
              {item.name}
            </Typography>
            <StarMark item={item} />
          </Box>
          {big && (
            <Box
              sx={{
                mt: 1.5,
                height: 110,
                borderRadius: 2,
                bgcolor: 'background.paper',
                display: 'grid',
                placeItems: 'center',
              }}
            >
              <FileIcon item={item} sx={{ fontSize: 56, opacity: 0.8 }} />
            </Box>
          )}
        </CardActionArea>
      </Card>
    );
  };

  const grid = (children: DriveItem[], big: boolean) => (
    <Box
      sx={{
        display: 'grid',
        gap: 1.5,
        gridTemplateColumns: `repeat(auto-fill, minmax(${big ? 200 : 180}px, 1fr))`,
      }}
    >
      {children.map((i) => card(i, big))}
    </Box>
  );

  return (
    <Box sx={{ display: 'grid', gap: 3 }}>
      {folders.length > 0 && (
        <section aria-label="Thư mục">
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Thư mục
          </Typography>
          {grid(folders, false)}
        </section>
      )}
      {files.length > 0 && (
        <section aria-label="Tệp">
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Tệp
          </Typography>
          {grid(files, true)}
        </section>
      )}
    </Box>
  );
}
