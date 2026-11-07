import { diffLines } from 'diff';
import type { Workbook } from 'exceljs';
import { columnName, worksheetSource } from '../office/sheetSource';

export interface DiffLine {
  kind: 'same' | 'added' | 'removed';
  text: string;
}

/** Một đoạn hiển thị: các dòng, hoặc một khối dòng không đổi được thu gọn. */
export type DiffBlock = { lines: DiffLine[] } | { collapsed: number };

export interface TextDiff {
  added: number;
  removed: number;
  blocks: DiffBlock[];
}

const CONTEXT = 3;

/**
 * So sánh hai văn bản theo dòng (jsdiff). Các khối dòng không đổi dài được thu gọn, chỉ giữ <c>CONTEXT</c> dòng quanh
 * chỗ thay đổi — như "git diff".
 */
export function compareText(before: string, after: string): TextDiff {
  // Cùng kiểu xuống dòng, luôn kết thúc bằng "\n": dòng cuối không bị coi là đổi chỉ vì thêm dòng phía sau nó.
  const normalize = (text: string) => {
    const lf = text.replace(/\r\n/g, '\n');
    return lf === '' || lf.endsWith('\n') ? lf : `${lf}\n`;
  };
  const lines: DiffLine[] = [];
  for (const part of diffLines(normalize(before), normalize(after))) {
    const kind: DiffLine['kind'] = part.added ? 'added' : part.removed ? 'removed' : 'same';
    const texts = part.value.endsWith('\n') ? part.value.slice(0, -1).split('\n') : part.value.split('\n');
    for (const text of texts) lines.push({ kind, text });
  }

  const blocks: DiffBlock[] = [];
  let current: DiffLine[] = [];
  let i = 0;
  while (i < lines.length) {
    if (lines[i]!.kind !== 'same') {
      current.push(lines[i]!);
      i++;
      continue;
    }

    let j = i;
    while (j < lines.length && lines[j]!.kind === 'same') j++;
    const run = lines.slice(i, j);
    const atStart = i === 0;
    const atEnd = j === lines.length;
    const keepHead = atStart ? 0 : CONTEXT;
    const keepTail = atEnd ? 0 : CONTEXT;
    if (run.length > keepHead + keepTail + 1) {
      current.push(...run.slice(0, keepHead));
      if (current.length > 0) blocks.push({ lines: current });
      blocks.push({ collapsed: run.length - keepHead - keepTail });
      current = run.slice(run.length - keepTail);
    } else {
      current.push(...run);
    }

    i = j;
  }

  if (current.length > 0) blocks.push({ lines: current });
  return {
    added: lines.filter((l) => l.kind === 'added').length,
    removed: lines.filter((l) => l.kind === 'removed').length,
    blocks,
  };
}

export interface CellChange {
  sheet: string;
  cell: string;
  before: string;
  after: string;
}

export interface SheetDiff {
  changes: CellChange[];
  /** Trang tính chỉ có ở bản sau / bản trước. */
  addedSheets: string[];
  removedSheets: string[];
  truncated: boolean;
}

const MAX_CHANGES = 1000;

/** So sánh hai workbook theo từng ô (nội dung gõ vào: giá trị hoặc công thức), mọi trang tính. */
export function compareWorkbooks(before: Workbook, after: Workbook): SheetDiff {
  const beforeNames = before.worksheets.map((w) => w.name);
  const afterNames = after.worksheets.map((w) => w.name);
  const changes: CellChange[] = [];
  let truncated = false;

  for (const name of afterNames.filter((n) => beforeNames.includes(n))) {
    const a = worksheetSource(before.getWorksheet(name)!);
    const b = worksheetSource(after.getWorksheet(name)!);
    const rows = Math.max(a.rowCount(), b.rowCount());
    const cols = Math.max(a.columnCount(), b.columnCount());
    for (let r = 1; r <= rows && !truncated; r++) {
      for (let c = 1; c <= cols; c++) {
        const old = a.raw(r, c);
        const now = b.raw(r, c);
        if (old !== now) {
          if (changes.length >= MAX_CHANGES) {
            truncated = true;
            break;
          }

          changes.push({ sheet: name, cell: `${columnName(c)}${r}`, before: old, after: now });
        }
      }
    }
  }

  return {
    changes,
    addedSheets: afterNames.filter((n) => !beforeNames.includes(n)),
    removedSheets: beforeNames.filter((n) => !afterNames.includes(n)),
    truncated,
  };
}
