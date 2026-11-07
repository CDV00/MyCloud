import AddIcon from '@mui/icons-material/Add';
import { Alert, Box, CircularProgress, IconButton, Tab, Tabs, Tooltip } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { detectDelimiter, parseCsv, toCsv } from '../csv';
import { SheetGrid } from '../SheetGrid';
import { csvSource, worksheetSource } from '../sheetSource';
import { BOM } from '../content';
import type { EditorProps } from './types';
import { loadWorkbook, saveWorkbook } from '../workbookIo';

/** CSV: một trang tính; lưu lại cùng dấu phân cách và BOM như tệp gốc. */
export function CsvEditor({ bytes, name, onDirty, register }: EditorProps) {
  const model = useMemo(() => {
    const text = new TextDecoder('utf-8').decode(bytes);
    const delimiter = detectDelimiter(text, name);
    return { rows: parseCsv(text, delimiter), delimiter, bom: text.charCodeAt(0) === 0xfeff };
  }, [bytes, name]);
  const source = useMemo(() => csvSource(model.rows), [model]);

  useEffect(() => {
    register(
      async () =>
        new Blob([model.bom ? BOM : '', toCsv(model.rows, model.delimiter)], {
          type: 'text/csv;charset=utf-8',
        }),
    );
  }, [model, register]);

  return <SheetGrid source={source} readOnly={false} onChange={onDirty} />;
}

/**
 * Excel (.xlsx): sửa giá trị / công thức ô trên workbook ExcelJS đã nạp — định dạng, độ rộng cột, ô gộp, các trang
 * tính khác được giữ khi ghi lại. Excel tính lại công thức khi mở tệp (fullCalcOnLoad).
 */
export default function SheetEditor({ bytes, onDirty, register }: EditorProps) {
  const workbook = useQuery({
    queryKey: ['sheet-editor', bytes],
    staleTime: Infinity,
    gcTime: 0,
    queryFn: async () => {
      const loaded = await loadWorkbook(bytes);
      if (loaded.workbook.worksheets.length === 0) loaded.workbook.addWorksheet('Trang tính 1');
      return loaded;
    },
  });
  const [active, setActive] = useState(0);
  const [, setSheetCount] = useState(0);

  useEffect(() => {
    const loaded = workbook.data;
    if (!loaded) return;
    // Lưu lại đúng định dạng đã đọc (tệp .xls cũ vẫn là .xls).
    register(() => saveWorkbook(loaded));
  }, [workbook.data, register]);

  if (workbook.error) {
    return <Alert severity="error">Không mở được bảng tính này (tệp có thể bị hỏng hoặc có mật khẩu).</Alert>;
  }

  if (!workbook.data) return <CircularProgress sx={{ m: 'auto' }} />;
  const book = workbook.data.workbook;
  const sheets = book.worksheets;
  const index = Math.min(active, sheets.length - 1);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {workbook.data.format === 'legacy' && (
        <Alert severity="info" sx={{ borderRadius: 0, py: 0 }}>
          Tệp Excel định dạng cũ (.xls): khi lưu chỉ giữ dữ liệu và công thức, không giữ định dạng ô — bản
          trước vẫn còn trong “Lịch sử & trao đổi”.
        </Alert>
      )}
      <Box sx={{ flexGrow: 1, minHeight: 0 }}>
        <SheetGrid
          key={sheets[index]!.id}
          source={worksheetSource(sheets[index]!)}
          readOnly={false}
          onChange={onDirty}
        />
      </Box>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          borderTop: 1,
          borderColor: 'divider',
          bgcolor: 'background.default',
        }}
      >
        <Tooltip title="Thêm trang tính">
          <IconButton
            size="small"
            aria-label="Thêm trang tính"
            sx={{ mx: 0.5 }}
            onClick={() => {
              let n = sheets.length + 1;
              while (book.getWorksheet(`Trang tính ${n}`)) n++;
              book.addWorksheet(`Trang tính ${n}`);
              setSheetCount(sheets.length);
              setActive(sheets.length - 1);
              onDirty();
            }}
          >
            <AddIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tabs
          value={index}
          onChange={(_, v: number) => setActive(v)}
          variant="scrollable"
          sx={{ minHeight: 36 }}
        >
          {sheets.map((s) => (
            <Tab key={s.id} label={s.name} sx={{ minHeight: 36, textTransform: 'none' }} />
          ))}
        </Tabs>
      </Box>
    </Box>
  );
}
