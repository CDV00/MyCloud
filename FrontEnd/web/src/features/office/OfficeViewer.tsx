import { Alert, Box, CircularProgress, Tab, Tabs } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { renderAsync } from 'docx-preview';
import { useEffect, useRef, useState } from 'react';
import { detectDelimiter, parseCsv } from './csv';
import { decodeText, fetchBytes } from './content';
import { SheetGrid } from './SheetGrid';
import { loadWorkbook } from './workbookIo';
import { csvSource, worksheetSource, type GridSource } from './sheetSource';

/**
 * Xem Word / Excel / CSV ngay trong trình duyệt (module tải lười, chỉ khi mở). Word dựng bằng docx-preview (giữ bố cục
 * trang, bảng, ảnh); Excel bằng ExcelJS (giá trị / kết quả công thức, nhiều trang tính).
 */
export default function OfficeViewer({
  kind,
  url,
  name,
}: {
  kind: 'docx' | 'sheet' | 'csv';
  url: string;
  name: string;
}) {
  const content = useQuery({
    queryKey: ['office-bytes', url],
    queryFn: () => fetchBytes(url),
    staleTime: Infinity,
  });

  if (content.error) return <Alert severity="error">Không đọc được nội dung tệp.</Alert>;
  if (!content.data) return <CircularProgress sx={{ color: 'grey.300' }} />;
  if (kind === 'docx') return <DocxView bytes={content.data} />;
  return <SheetView kind={kind} bytes={content.data} name={name} />;
}

function DocxView({ bytes }: { bytes: ArrayBuffer }) {
  const container = useRef<HTMLDivElement>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const target = container.current;
    if (!target) return;
    target.innerHTML = '';
    // Không cho tài liệu chạy script / tải tài nguyên ngoài: docx-preview chỉ dựng HTML tĩnh từ XML của tệp.
    renderAsync(bytes, target, undefined, {
      inWrapper: true,
      ignoreLastRenderedPageBreak: true,
      experimental: false,
      useBase64URL: true,
      renderComments: false,
    }).catch(() => setFailed(true));
  }, [bytes]);

  if (failed)
    return (
      <Alert severity="error">
        Không mở được tài liệu Word này (có thể tệp bị hỏng hoặc là định dạng .doc cũ).
      </Alert>
    );
  return (
    <Box
      ref={container}
      sx={{
        width: '100%',
        // docx-preview dựng từng trang trên nền xám; giữ màu chữ của tài liệu (không theo giao diện tối).
        '& .docx-wrapper': { bgcolor: 'transparent', p: { xs: 0, sm: 2 } },
        '& .docx-wrapper > section.docx': { boxShadow: '0 1px 4px rgba(0,0,0,.4)', mb: 2, maxWidth: '100%' },
      }}
    />
  );
}

interface LoadedSheets {
  names: string[];
  sources: GridSource[];
}

async function loadSheets(kind: 'sheet' | 'csv', bytes: ArrayBuffer, name: string): Promise<LoadedSheets> {
  if (kind === 'csv') {
    const text = decodeText(bytes);
    return { names: [name], sources: [csvSource(parseCsv(text, detectDelimiter(text, name)))] };
  }

  const { workbook } = await loadWorkbook(bytes);
  const sheets = workbook.worksheets.filter((w) => w.state !== 'hidden' && w.state !== 'veryHidden');
  return { names: sheets.map((w) => w.name), sources: sheets.map(worksheetSource) };
}

function SheetView({ kind, bytes, name }: { kind: 'sheet' | 'csv'; bytes: ArrayBuffer; name: string }) {
  const sheets = useQuery({
    queryKey: ['office-sheets', kind, bytes],
    queryFn: () => loadSheets(kind, bytes, name),
    staleTime: Infinity,
  });
  const [active, setActive] = useState(0);

  if (sheets.error)
    return (
      <Alert severity="error">
        Không mở được bảng tính này (có thể tệp bị hỏng hoặc là định dạng .xls cũ).
      </Alert>
    );
  if (!sheets.data) return <CircularProgress sx={{ color: 'grey.300' }} />;
  const { names, sources } = sheets.data;
  if (sources.length === 0) return <Alert severity="info">Bảng tính không có trang tính nào.</Alert>;

  return (
    <Box
      sx={{
        width: '100%',
        height: 'calc(100vh - 88px)',
        display: 'flex',
        flexDirection: 'column',
        bgcolor: 'background.paper',
        color: 'text.primary',
        borderRadius: 1,
        overflow: 'hidden',
      }}
    >
      <Box sx={{ flexGrow: 1, minHeight: 0 }}>
        <SheetGrid key={active} source={sources[Math.min(active, sources.length - 1)]!} readOnly />
      </Box>
      {names.length > 1 && (
        <Tabs
          value={Math.min(active, sources.length - 1)}
          onChange={(_, v: number) => setActive(v)}
          variant="scrollable"
          sx={{ borderTop: 1, borderColor: 'divider', minHeight: 36 }}
        >
          {names.map((n) => (
            <Tab key={n} label={n} sx={{ minHeight: 36, textTransform: 'none' }} />
          ))}
        </Tabs>
      )}
    </Box>
  );
}
