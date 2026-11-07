import type { CellValue, Worksheet } from 'exceljs';

/** Nguồn dữ liệu của lưới ô — chung cho trang tính Excel và CSV. Hàng / cột tính từ 1 (như Excel). */
export interface GridSource {
  rowCount(): number;
  columnCount(): number;
  /** Giá trị hiển thị (kết quả công thức, ngày đã định dạng). */
  display(row: number, col: number): string;
  /** Nội dung khi sửa: công thức có dấu "=" ở đầu. */
  raw(row: number, col: number): string;
  set(row: number, col: number, raw: string): void;
}

/** "A", "B", …, "Z", "AA"… */
export function columnName(col: number): string {
  let name = '';
  for (let n = col; n > 0; n = Math.floor((n - 1) / 26))
    name = String.fromCharCode(65 + ((n - 1) % 26)) + name;
  return name;
}

export function csvSource(rows: string[][]): GridSource {
  return {
    rowCount: () => rows.length,
    columnCount: () => rows.reduce((max, r) => Math.max(max, r.length), 0),
    display: (r, c) => rows[r - 1]?.[c - 1] ?? '',
    raw: (r, c) => rows[r - 1]?.[c - 1] ?? '',
    set(r, c, raw) {
      while (rows.length < r) rows.push([]);
      const row = rows[r - 1]!;
      while (row.length < c) row.push('');
      row[c - 1] = raw;
    },
  };
}

function formatValue(value: CellValue): string {
  if (value === null || value === undefined) return '';
  if (value instanceof Date) return value.toLocaleDateString('vi-VN');
  if (typeof value === 'number') return value.toLocaleString('vi-VN', { maximumFractionDigits: 10 });
  if (typeof value === 'object') {
    if ('formula' in value || 'sharedFormula' in value) {
      // Công thức vừa nhập trong trình duyệt chưa có kết quả (Excel tính khi mở tệp) ⇒ hiện chính công thức.
      const result = (value as { result?: CellValue }).result;
      return result === undefined && 'formula' in value ? `=${value.formula}` : formatValue(result ?? null);
    }
    if ('richText' in value) return value.richText.map((t) => t.text).join('');
    if ('text' in value) return String(value.text);
    if ('error' in value) return String(value.error);
  }

  return String(value);
}

function rawValue(value: CellValue): string {
  if (value === null || value === undefined) return '';
  if (value instanceof Date) return value.toISOString().slice(0, 10);
  if (typeof value === 'object') {
    if ('formula' in value && value.formula) return `=${value.formula}`;
    if ('sharedFormula' in value) return formatValue(value);
    if ('richText' in value || 'text' in value || 'error' in value) return formatValue(value);
  }

  return String(value);
}

/** Chuỗi người dùng gõ ⇒ giá trị ô: "=…" công thức, số, true/false, còn lại là văn bản. */
export function parseInput(raw: string): CellValue {
  const text = raw.trim();
  if (text === '') return null;
  if (text.startsWith('=') && text.length > 1) return { formula: text.slice(1) } as CellValue;
  if (/^-?\d+([.,]\d+)?$/.test(text)) return Number(text.replace(',', '.'));
  if (/^(true|false)$/i.test(text)) return text.toLowerCase() === 'true';
  return raw;
}

/**
 * Trang tính ExcelJS: sửa giá trị ô ngay trên workbook đã nạp ⇒ khi ghi lại, định dạng / độ rộng cột / công thức khác
 * được giữ nguyên (chỉ ô bị sửa thay đổi).
 */
export function worksheetSource(sheet: Worksheet): GridSource {
  return {
    rowCount: () => sheet.rowCount,
    columnCount: () => sheet.columnCount,
    // findCell không tạo ô mới (getCell tạo ⇒ chỉ cần hiển thị cũng làm phình trang tính).
    display: (r, c) => formatValue(sheet.findCell(r, c)?.value ?? null),
    raw: (r, c) => rawValue(sheet.findCell(r, c)?.value ?? null),
    set(r, c, raw) {
      sheet.getCell(r, c).value = parseInput(raw);
    },
  };
}
