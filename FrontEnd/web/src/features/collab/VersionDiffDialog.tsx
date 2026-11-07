import CloseIcon from '@mui/icons-material/Close';
import {
  Alert,
  Box,
  Chip,
  CircularProgress,
  Dialog,
  DialogContent,
  DialogTitle,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import mammoth from 'mammoth';
import { getDownloadUrl, type DriveItem, type FileVersion } from '../../api/drive';
import { decodeText, fetchBytes } from '../office/content';
import { loadWorkbook } from '../office/workbookIo';
import { previewKind } from '../preview/previewKind';
import { compareText, compareWorkbooks, type SheetDiff, type TextDiff } from './diffing';

type Comparison =
  { kind: 'text'; diff: TextDiff } | { kind: 'sheet'; diff: SheetDiff } | { kind: 'unsupported' };

async function versionBytes(itemId: string, versionId: string) {
  return fetchBytes((await getDownloadUrl(itemId, { inline: true, versionId })).url);
}

async function compare(item: DriveItem, older: FileVersion, newer: FileVersion): Promise<Comparison> {
  const kind = previewKind({
    ...item,
    mimeType: newer.mimeType,
    sizeBytes: Math.max(older.sizeBytes, newer.sizeBytes),
  });
  if (kind !== 'text' && kind !== 'csv' && kind !== 'docx' && kind !== 'sheet')
    return { kind: 'unsupported' };

  const [a, b] = await Promise.all([versionBytes(item.id, older.id), versionBytes(item.id, newer.id)]);
  if (kind === 'sheet') {
    const [wa, wb] = await Promise.all([loadWorkbook(a), loadWorkbook(b)]);
    return { kind: 'sheet', diff: compareWorkbooks(wa.workbook, wb.workbook) };
  }

  if (kind === 'docx') {
    // So theo đoạn văn của nội dung chữ (định dạng không được so).
    const [ta, tb] = await Promise.all([
      mammoth.extractRawText({ arrayBuffer: a }),
      mammoth.extractRawText({ arrayBuffer: b }),
    ]);
    const paragraphs = (t: string) =>
      t
        .split(/\n+/)
        .map((p) => p.trim())
        .filter(Boolean)
        .join('\n');
    return { kind: 'text', diff: compareText(paragraphs(ta.value), paragraphs(tb.value)) };
  }

  return { kind: 'text', diff: compareText(decodeText(a), decodeText(b)) };
}

/**
 * "Thay đổi gì?" giữa hai phiên bản (ADR-017): văn bản / mã / CSV theo dòng, Word theo đoạn văn, Excel theo ô.
 * Ảnh, PDF, video… không so được — xem từng bản.
 */
export default function VersionDiffDialog({
  item,
  older,
  newer,
  onClose,
}: {
  item: DriveItem;
  older: FileVersion;
  newer: FileVersion;
  onClose(): void;
}) {
  const result = useQuery({
    queryKey: ['version-diff', item.id, older.id, newer.id],
    queryFn: () => compare(item, older, newer),
    staleTime: Infinity,
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="lg" scroll="paper">
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 6 }}>
        So sánh phiên bản {older.versionNo} → {newer.versionNo}
        <Typography component="span" color="text.secondary" noWrap sx={{ ml: 1 }}>
          {item.name}
        </Typography>
        <IconButton onClick={onClose} aria-label="Đóng" sx={{ position: 'absolute', right: 8, top: 8 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        {result.error && <Alert severity="error">Không tải được nội dung để so sánh.</Alert>}
        {result.isPending && <CircularProgress />}
        {result.data?.kind === 'unsupported' && (
          <Alert severity="info">Loại tệp này không so sánh được — hãy xem từng phiên bản.</Alert>
        )}
        {result.data?.kind === 'text' && <TextDiffView diff={result.data.diff} />}
        {result.data?.kind === 'sheet' && <SheetDiffView diff={result.data.diff} />}
      </DialogContent>
    </Dialog>
  );
}

function TextDiffView({ diff }: { diff: TextDiff }) {
  if (diff.added === 0 && diff.removed === 0)
    return <Alert severity="success">Nội dung chữ không thay đổi.</Alert>;
  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1}>
        <Chip size="small" color="success" variant="outlined" label={`+${diff.added} dòng`} />
        <Chip size="small" color="error" variant="outlined" label={`−${diff.removed} dòng`} />
      </Stack>
      <Box
        component="pre"
        sx={{
          m: 0,
          fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace',
          fontSize: 13,
          lineHeight: 1.55,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          border: 1,
          borderColor: 'divider',
          borderRadius: 1,
          overflow: 'hidden',
        }}
      >
        {diff.blocks.map((block, i) =>
          'collapsed' in block ? (
            <Box key={i} sx={{ px: 1.5, py: 0.25, bgcolor: 'action.hover', color: 'text.secondary' }}>
              … {block.collapsed} dòng không đổi
            </Box>
          ) : (
            block.lines.map((line, j) => (
              <Box
                key={`${i}-${j}`}
                sx={{
                  px: 1.5,
                  bgcolor:
                    line.kind === 'added'
                      ? 'rgba(46,160,67,.15)'
                      : line.kind === 'removed'
                        ? 'rgba(248,81,73,.15)'
                        : undefined,
                }}
              >
                <Box
                  component="span"
                  sx={{ display: 'inline-block', width: 16, color: 'text.secondary', userSelect: 'none' }}
                >
                  {line.kind === 'added' ? '+' : line.kind === 'removed' ? '−' : ' '}
                </Box>
                {line.text || ' '}
              </Box>
            ))
          ),
        )}
      </Box>
    </Stack>
  );
}

function SheetDiffView({ diff }: { diff: SheetDiff }) {
  const nothing =
    diff.changes.length === 0 && diff.addedSheets.length === 0 && diff.removedSheets.length === 0;
  if (nothing) return <Alert severity="success">Không có ô nào thay đổi.</Alert>;
  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
        <Chip
          size="small"
          variant="outlined"
          label={`${diff.changes.length}${diff.truncated ? '+' : ''} ô thay đổi`}
        />
        {diff.addedSheets.map((s) => (
          <Chip key={s} size="small" color="success" variant="outlined" label={`Thêm trang tính “${s}”`} />
        ))}
        {diff.removedSheets.map((s) => (
          <Chip key={s} size="small" color="error" variant="outlined" label={`Xoá trang tính “${s}”`} />
        ))}
      </Stack>
      {diff.changes.length > 0 && (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Trang tính</TableCell>
              <TableCell>Ô</TableCell>
              <TableCell>Trước</TableCell>
              <TableCell>Sau</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {diff.changes.map((c) => (
              <TableRow key={`${c.sheet}!${c.cell}`}>
                <TableCell>{c.sheet}</TableCell>
                <TableCell sx={{ fontFamily: 'monospace' }}>{c.cell}</TableCell>
                <TableCell sx={{ bgcolor: c.before ? 'rgba(248,81,73,.12)' : undefined }}>
                  {c.before || '—'}
                </TableCell>
                <TableCell sx={{ bgcolor: c.after ? 'rgba(46,160,67,.12)' : undefined }}>
                  {c.after || '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {diff.truncated && (
        <Typography variant="body2" color="text.secondary">
          Chỉ hiển thị 1000 ô thay đổi đầu tiên.
        </Typography>
      )}
    </Stack>
  );
}
