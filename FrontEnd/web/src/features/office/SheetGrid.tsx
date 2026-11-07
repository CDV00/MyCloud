import { Box, InputBase, Typography } from '@mui/material';
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { columnName, type GridSource } from './sheetSource';

const MAX_RENDER_ROWS = 2000;
const MAX_RENDER_COLS = 200;
const CELL_WIDTH = 120;
const ROW_HEIGHT = 26;

/**
 * Lưới ô kiểu bảng tính (xem / sửa giá trị). Phím mũi tên / Tab / Enter để di chuyển, gõ để thay nội dung, Enter hoặc
 * nhấp đúp để sửa, Delete để xoá, Esc để huỷ. Công thức hiển thị kết quả trong ô và nội dung "=…" ở thanh công thức.
 * Luôn có thêm vài hàng / cột trống để gõ thêm dữ liệu.
 */
export function SheetGrid({
  source,
  readOnly,
  onChange,
}: {
  source: GridSource;
  readOnly: boolean;
  onChange?(): void;
}) {
  // Vị trí chọn và nội dung đang sửa được giữ cả trong ref: phím gõ nhanh có thể tới trước khi React vẽ lại (vd gõ
  // "Tháng⇥Doanh thu" liền một mạch) — trình xử lý phím phải thấy trạng thái mới nhất, không phải bản của lần vẽ trước.
  const [selected, setSelectedState] = useState({ row: 1, col: 1 });
  const selectedRef = useRef(selected);
  const setSelected = (next: { row: number; col: number }) => {
    selectedRef.current = next;
    setSelectedState(next);
  };
  // Nội dung đang sửa và nơi sửa: trong ô hay trên thanh công thức.
  const [edit, setEditState] = useState<{ value: string; origin: 'cell' | 'bar' } | null>(null);
  const editRef = useRef(edit);
  const setEdit = (next: { value: string; origin: 'cell' | 'bar' } | null) => {
    editRef.current = next;
    setEditState(next);
  };
  const editing = edit?.value ?? null;
  const setEditing = (value: string | null, origin: 'cell' | 'bar' = 'cell') =>
    setEdit(value === null ? null : { value, origin });
  const [, setVersion] = useState(0);
  const container = useRef<HTMLDivElement>(null);

  const totalRows = Math.min(
    MAX_RENDER_ROWS,
    Math.max(source.rowCount() + (readOnly ? 0 : 20), readOnly ? 1 : 50),
  );
  const totalCols = Math.min(
    MAX_RENDER_COLS,
    Math.max(source.columnCount() + (readOnly ? 0 : 5), readOnly ? 1 : 12),
  );
  const truncated = source.rowCount() > MAX_RENDER_ROWS || source.columnCount() > MAX_RENDER_COLS;

  useEffect(() => {
    if (editing === null) container.current?.focus({ preventScroll: true });
  }, [editing]);

  useEffect(() => {
    container.current
      ?.querySelector(`[data-cell="${selected.row}:${selected.col}"]`)
      ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [selected]);

  const move = (dRow: number, dCol: number) => {
    const s = selectedRef.current;
    setSelected({
      row: Math.min(totalRows, Math.max(1, s.row + dRow)),
      col: Math.min(totalCols, Math.max(1, s.col + dCol)),
    });
  };

  const commit = (value: string, then?: () => void) => {
    const { row, col } = selectedRef.current;
    if (!readOnly && value !== source.raw(row, col)) {
      source.set(row, col, value);
      setVersion((v) => v + 1);
      onChange?.();
    }

    setEditing(null);
    then?.();
  };

  function onGridKey(e: KeyboardEvent) {
    // Phím trong ô nhập / thanh công thức nổi bọt lên đây: chúng đã được xử lý ở đó (tránh Tab / Enter đi hai lần).
    if (e.target !== container.current) return;
    const pending = editRef.current;
    if (pending?.origin === 'bar') return;
    if (pending) {
      // Đang bắt đầu sửa ô nhưng ô nhập chưa kịp nhận focus: phím vẫn thuộc về nội dung đang sửa.
      e.preventDefault();
      if (e.key === 'Enter') commit(pending.value, () => move(1, 0));
      else if (e.key === 'Tab') commit(pending.value, () => move(0, e.shiftKey ? -1 : 1));
      else if (e.key === 'Escape') setEditing(null);
      else if (e.key === 'Backspace') setEditing(pending.value.slice(0, -1));
      else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) setEditing(pending.value + e.key);
      return;
    }

    const { row, col } = selectedRef.current;
    const keys: Record<string, () => void> = {
      ArrowUp: () => move(-1, 0),
      ArrowDown: () => move(1, 0),
      ArrowLeft: () => move(0, -1),
      ArrowRight: () => move(0, 1),
      Tab: () => move(0, e.shiftKey ? -1 : 1),
      Enter: () => (readOnly ? move(1, 0) : setEditing(source.raw(row, col))),
      F2: () => !readOnly && setEditing(source.raw(row, col)),
      Delete: () => !readOnly && commit(''),
      Backspace: () => !readOnly && commit(''),
    };
    const action = keys[e.key];
    if (action) {
      e.preventDefault();
      action();
    } else if (!readOnly && e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      e.preventDefault();
      setEditing(e.key); // gõ ký tự ⇒ thay nội dung ô
    }
  }

  function onEditorKey(e: KeyboardEvent<HTMLInputElement>) {
    const value = (e.target as HTMLInputElement).value;
    if (e.key === 'Enter') {
      e.preventDefault();
      commit(value, () => move(1, 0));
    } else if (e.key === 'Tab') {
      e.preventDefault();
      commit(value, () => move(0, e.shiftKey ? -1 : 1));
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation(); // không đóng hộp thoại
      setEditing(null);
    }
  }

  const header = {
    position: 'sticky' as const,
    bgcolor: 'background.default',
    color: 'text.secondary',
    fontSize: 12,
    textAlign: 'center' as const,
    border: 1,
    borderColor: 'divider',
    userSelect: 'none' as const,
    zIndex: 1,
  };

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        minHeight: 0,
        height: '100%',
        bgcolor: 'background.paper',
      }}
    >
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap: 1,
          px: 1,
          py: 0.5,
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="body2" sx={{ width: 64, color: 'text.secondary', fontFamily: 'monospace' }}>
          {columnName(selected.col)}
          {selected.row}
        </Typography>
        <InputBase
          key={`${selected.row}:${selected.col}:${edit?.origin === 'cell' ? 'cell' : edit === null}`}
          defaultValue={editing ?? source.raw(selected.row, selected.col)}
          readOnly={readOnly}
          placeholder={readOnly ? '' : 'Nhập giá trị hoặc công thức (=SUM(A1:A3))'}
          onKeyDown={onEditorKey}
          onBlur={(e) => editing !== null && commit(e.target.value)}
          onFocus={(e) => !readOnly && editing === null && setEditing(e.target.value, 'bar')}
          inputProps={{ 'aria-label': 'Thanh công thức' }}
          sx={{ flexGrow: 1, fontFamily: 'monospace', fontSize: 13 }}
        />
      </Box>
      <Box
        ref={container}
        tabIndex={0}
        role="grid"
        aria-label="Trang tính"
        aria-readonly={readOnly}
        onKeyDown={onGridKey}
        sx={{ flexGrow: 1, overflow: 'auto', outline: 'none', position: 'relative' }}
      >
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: `48px repeat(${totalCols}, ${CELL_WIDTH}px)`,
            gridAutoRows: `${ROW_HEIGHT}px`,
            width: 'max-content',
          }}
        >
          <Box sx={{ ...header, top: 0, left: 0, zIndex: 2 }} />
          {Array.from({ length: totalCols }, (_, i) => (
            <Box key={`h${i}`} role="columnheader" sx={{ ...header, top: 0, lineHeight: `${ROW_HEIGHT}px` }}>
              {columnName(i + 1)}
            </Box>
          ))}
          {Array.from({ length: totalRows }, (_, r) => {
            const row = r + 1;
            return [
              <Box
                key={`r${row}`}
                role="rowheader"
                sx={{ ...header, left: 0, lineHeight: `${ROW_HEIGHT}px` }}
              >
                {row}
              </Box>,
              ...Array.from({ length: totalCols }, (_, c) => {
                const col = c + 1;
                const isSelected = selected.row === row && selected.col === col;
                const text = source.display(row, col);
                return (
                  <Box
                    key={`${row}:${col}`}
                    data-cell={`${row}:${col}`}
                    role="gridcell"
                    aria-selected={isSelected}
                    onMouseDown={() => {
                      if (editing !== null) return;
                      setSelected({ row, col });
                    }}
                    onDoubleClick={() => !readOnly && setEditing(source.raw(row, col))}
                    title={text.length > 15 ? text : undefined}
                    sx={{
                      borderRight: 1,
                      borderBottom: 1,
                      borderColor: 'divider',
                      px: 0.75,
                      fontSize: 13,
                      lineHeight: `${ROW_HEIGHT - 1}px`,
                      whiteSpace: 'nowrap',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      textAlign: /^-?[\d.,\s]+%?$/.test(text) ? 'right' : 'left',
                      outline: isSelected ? '2px solid' : 'none',
                      outlineColor: 'primary.main',
                      outlineOffset: -2,
                      position: 'relative',
                      cursor: 'cell',
                    }}
                  >
                    {isSelected && edit?.origin === 'cell' ? (
                      <InputBase
                        autoFocus
                        value={edit.value}
                        onChange={(e) => setEditing(e.target.value)}
                        // Con trỏ ở cuối nội dung đã gõ (không chọn / đặt về đầu).
                        onFocus={(e) =>
                          e.target.setSelectionRange(e.target.value.length, e.target.value.length)
                        }
                        onKeyDown={onEditorKey}
                        onBlur={(e) => commit(e.target.value)}
                        inputProps={{ 'aria-label': `Sửa ô ${columnName(col)}${row}` }}
                        sx={{
                          position: 'absolute',
                          inset: 0,
                          px: 0.75,
                          fontSize: 13,
                          bgcolor: 'background.paper',
                        }}
                      />
                    ) : (
                      text
                    )}
                  </Box>
                );
              }),
            ];
          })}
        </Box>
      </Box>
      {truncated && (
        <Typography variant="caption" color="text.secondary" sx={{ px: 1, py: 0.5 }}>
          Chỉ hiển thị {MAX_RENDER_ROWS} hàng × {MAX_RENDER_COLS} cột đầu tiên.
        </Typography>
      )}
    </Box>
  );
}
