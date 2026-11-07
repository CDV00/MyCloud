import { Workbook } from 'exceljs';
import JSZip from 'jszip';

/**
 * Định dạng nội dung thật của tệp bảng tính (đuôi tệp không đáng tin: nhiều hệ thống xuất ".xls" mà bên trong là xlsx,
 * HTML hoặc XML Spreadsheet 2003).
 * - `xlsx`: Office Open XML (zip) — đọc / ghi bằng ExcelJS, giữ định dạng.
 * - `legacy`: .xls nhị phân (BIFF), HTML, XML 2003… — đọc bằng SheetJS, khi lưu ghi lại thành .xls (chỉ giữ dữ liệu và
 *   công thức, không giữ định dạng).
 */
export type WorkbookFormat = 'xlsx' | 'legacy';

export interface LoadedWorkbook {
  workbook: Workbook;
  format: WorkbookFormat;
}

const SPREADSHEET_NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';

const isZip = (bytes: Uint8Array) =>
  bytes[0] === 0x50 && bytes[1] === 0x4b && bytes[2] === 0x03 && bytes[3] === 0x04;

/**
 * Bỏ tiền tố namespace của SpreadsheetML (`<x:workbook xmlns:x="…/main">` ⇒ `<workbook xmlns="…/main">`). Tệp do
 * OpenXML SDK (.NET) và một số hệ thống báo cáo tạo ra dùng tiền tố; Excel đọc được nhưng ExcelJS thì không.
 */
export async function normalizeOoxml(bytes: ArrayBuffer): Promise<ArrayBuffer> {
  const zip = await JSZip.loadAsync(bytes);
  let changed = false;
  for (const name of Object.keys(zip.files)) {
    if (!name.startsWith('xl/') || !name.endsWith('.xml')) continue;
    const xml = await zip.file(name)!.async('string');
    let fixed = xml;
    const prefix = new RegExp(`xmlns:([A-Za-z_][\\w.-]*)="${SPREADSHEET_NS.replace(/[.]/g, '\\.')}"`).exec(
      xml,
    )?.[1];
    if (prefix) {
      fixed = fixed
        .replace(new RegExp(`<(/?)${prefix}:`, 'g'), '<$1')
        .replace(`xmlns:${prefix}="${SPREADSHEET_NS}"`, `xmlns="${SPREADSHEET_NS}"`);
    }

    if (name.startsWith('xl/worksheets/')) fixed = addCellReferences(fixed);
    if (fixed !== xml) {
      zip.file(name, fixed);
      changed = true;
    }
  }

  return changed ? zip.generateAsync({ type: 'arraybuffer' }) : bytes;
}

const columnLetters = (col: number) => {
  let name = '';
  for (let n = col; n > 0; n = Math.floor((n - 1) / 26))
    name = String.fromCharCode(65 + ((n - 1) % 26)) + name;
  return name;
};

const columnNumber = (letters: string) => [...letters].reduce((n, c) => n * 26 + c.charCodeAt(0) - 64, 0);

/**
 * Điền thuộc tính `r` còn thiếu của hàng (`<row r="3">`) và ô (`<c r="B3">`). Chuẩn cho phép bỏ (ô nằm kế ô trước, hàng kế
 * hàng trước) và OpenXML SDK thường bỏ; Excel tự suy ra nhưng ExcelJS cần địa chỉ tường minh.
 */
export function addCellReferences(xml: string): string {
  let rowNumber = 0;
  return xml.replace(/<row\b([^>]*?)(?:\/>|>([\s\S]*?)<\/row>)/g, (_row, attrs: string, body?: string) => {
    const explicitRow = /\br="(\d+)"/.exec(attrs);
    rowNumber = explicitRow ? Number(explicitRow[1]) : rowNumber + 1;
    const rowAttrs = explicitRow ? attrs : `${attrs} r="${rowNumber}"`;
    if (body === undefined) return `<row${rowAttrs}/>`;

    let col = 0;
    const cells = body.replace(/<c\b([^>]*?)(\/?)>/g, (_cell, cellAttrs: string, close: string) => {
      const explicit = /\br="([A-Z]+)\d+"/.exec(cellAttrs);
      col = explicit ? columnNumber(explicit[1]!) : col + 1;
      return explicit
        ? `<c${cellAttrs}${close}>`
        : `<c r="${columnLetters(col)}${rowNumber}"${cellAttrs}${close}>`;
    });
    return `<row${rowAttrs}>${cells}</row>`;
  });
}

export async function loadWorkbook(bytes: ArrayBuffer): Promise<LoadedWorkbook> {
  const workbook = new Workbook();
  if (isZip(new Uint8Array(bytes, 0, Math.min(4, bytes.byteLength)))) {
    await workbook.xlsx.load(await normalizeOoxml(bytes));
    return { workbook, format: 'xlsx' };
  }

  // .xls cũ / HTML / XML 2003: SheetJS đọc rồi chuyển sang xlsx để dùng chung lưới ô và trình sửa.
  const XLSX = await import('xlsx');
  const sheet = XLSX.read(new Uint8Array(bytes), {
    type: 'array',
    cellFormula: true,
    cellDates: true,
    dense: false,
  });
  const converted = XLSX.write(sheet, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
  await workbook.xlsx.load(converted);
  return { workbook, format: 'legacy' };
}

export const XLS_MIME = 'application/vnd.ms-excel';

/** Ghi lại đúng định dạng đã đọc (xlsx ⇒ xlsx; .xls cũ ⇒ .xls BIFF8). */
export async function saveWorkbook({ workbook, format }: LoadedWorkbook): Promise<Blob> {
  workbook.calcProperties.fullCalcOnLoad = true;
  const xlsx = await workbook.xlsx.writeBuffer();
  if (format === 'xlsx') {
    return new Blob([xlsx], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  }

  const XLSX = await import('xlsx');
  const sheet = XLSX.read(new Uint8Array(xlsx as ArrayBuffer), {
    type: 'array',
    cellFormula: true,
    cellDates: true,
  });
  return new Blob([XLSX.write(sheet, { bookType: 'xls', type: 'array' }) as ArrayBuffer], { type: XLS_MIME });
}
