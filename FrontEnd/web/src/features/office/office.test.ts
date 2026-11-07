import { Workbook } from 'exceljs';
import JSZip from 'jszip';
import mammoth from 'mammoth';
import * as XLSX from 'xlsx';
import { describe, expect, it } from 'vitest';
import { editKind, previewKind } from '../preview/previewKind';
import { BOM } from './content';
import { detectDelimiter, parseCsv, toCsv } from './csv';
import { toDocx } from './docxExport';
import { columnName, csvSource, parseInput, worksheetSource } from './sheetSource';
import { loadWorkbook, saveWorkbook } from './workbookIo';

const file = (name: string, mimeType: string | null, sizeBytes = 100) => ({
  kind: 'file' as const,
  name,
  mimeType,
  sizeBytes,
});

/** jsdom chưa có Blob.arrayBuffer. */
const readBlob = (blob: Blob) =>
  new Promise<ArrayBuffer>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as ArrayBuffer);
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(blob);
  });

describe('CSV', () => {
  it('đọc trường có dấu ngoặc kép, xuống dòng trong ô, BOM và CRLF', () => {
    const text = BOM + 'Tên;Ghi chú\r\n"Nguyễn, An";"dòng 1\ndòng 2"\r\nBình;"nói ""xin chào"""\r\n';
    const delimiter = detectDelimiter(text);
    expect(delimiter).toBe(';');
    expect(parseCsv(text, delimiter)).toEqual([
      ['Tên', 'Ghi chú'],
      ['Nguyễn, An', 'dòng 1\ndòng 2'],
      ['Bình', 'nói "xin chào"'],
    ]);
  });

  it('ghi lại có trích dẫn khi cần và bỏ ô / dòng trống ở cuối', () => {
    const rows = [
      ['a', 'b,c', ''],
      ['x"y', '', ''],
      ['', '', ''],
    ];
    expect(toCsv(rows)).toBe('a,"b,c"\r\n"x""y"');
    expect(parseCsv(toCsv(rows))).toEqual([['a', 'b,c'], ['x"y']]);
  });

  it('nguồn lưới mở rộng hàng / cột khi gõ vào ô ngoài vùng dữ liệu', () => {
    const rows = [['1']];
    const source = csvSource(rows);
    source.set(3, 2, 'mới');
    expect(rows).toEqual([['1'], [], ['', 'mới']]);
    expect(source.rowCount()).toBe(3);
    expect(source.columnCount()).toBe(2);
  });
});

describe('ô bảng tính', () => {
  it('đặt tên cột như Excel', () => {
    expect([1, 26, 27, 52, 703].map(columnName)).toEqual(['A', 'Z', 'AA', 'AZ', 'AAA']);
  });

  it('chuyển nội dung gõ thành số, công thức, logic, văn bản', () => {
    expect(parseInput('42')).toBe(42);
    expect(parseInput('3,5')).toBe(3.5);
    expect(parseInput('=SUM(A1:A3)')).toEqual({ formula: 'SUM(A1:A3)' });
    expect(parseInput('TRUE')).toBe(true);
    expect(parseInput('  ')).toBeNull();
    expect(parseInput('Hà Nội')).toBe('Hà Nội');
  });

  it('sửa ô Excel giữ định dạng và công thức khác khi ghi lại', async () => {
    const original = new Workbook();
    const sheet = original.addWorksheet('Doanh thu');
    sheet.getCell('A1').value = 10;
    sheet.getCell('A1').font = { bold: true };
    sheet.getCell('A2').value = 20;
    sheet.getCell('A3').value = { formula: 'SUM(A1:A2)', result: 30 };
    const bytes = await original.xlsx.writeBuffer();

    const loaded = new Workbook();
    await loaded.xlsx.load(bytes);
    const source = worksheetSource(loaded.getWorksheet('Doanh thu')!);
    expect(source.display(3, 1)).toBe('30');
    expect(source.raw(3, 1)).toBe('=SUM(A1:A2)');
    expect(source.display(9, 9)).toBe(''); // không tạo ô mới khi chỉ đọc
    expect(loaded.getWorksheet('Doanh thu')!.rowCount).toBe(3);
    source.set(2, 1, '25');

    const saved = new Workbook();
    await saved.xlsx.load(await loaded.xlsx.writeBuffer());
    const again = saved.getWorksheet('Doanh thu')!;
    expect(again.getCell('A2').value).toBe(25);
    expect(again.getCell('A1').font?.bold).toBe(true);
    expect(again.getCell('A3').formula).toBe('SUM(A1:A2)');
  });
});

describe('Word', () => {
  it('lưu nội dung trình sửa thành .docx đọc lại được (tiêu đề, định dạng, danh sách, bảng)', async () => {
    const blob = await toDocx({
      type: 'doc',
      content: [
        { type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: 'Báo cáo quý' }] },
        {
          type: 'paragraph',
          content: [
            { type: 'text', text: 'Doanh thu ' },
            { type: 'text', text: 'tăng mạnh', marks: [{ type: 'bold' }] },
          ],
        },
        {
          type: 'bulletList',
          content: [
            {
              type: 'listItem',
              content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Hà Nội' }] }],
            },
            {
              type: 'listItem',
              content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Đà Nẵng' }] }],
            },
          ],
        },
        {
          type: 'table',
          content: [
            {
              type: 'tableRow',
              content: [
                {
                  type: 'tableHeader',
                  content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Tháng' }] }],
                },
                {
                  type: 'tableHeader',
                  content: [{ type: 'paragraph', content: [{ type: 'text', text: 'Số' }] }],
                },
              ],
            },
            {
              type: 'tableRow',
              content: [
                {
                  type: 'tableCell',
                  content: [{ type: 'paragraph', content: [{ type: 'text', text: '1' }] }],
                },
                { type: 'tableCell', content: [{ type: 'paragraph' }] },
              ],
            },
          ],
        },
      ],
    });

    const { value: html } = await mammoth.convertToHtml({ buffer: Buffer.from(await readBlob(blob)) }); // Node: mammoth đọc buffer, trình duyệt đọc arrayBuffer;
    expect(html).toContain('<h1>Báo cáo quý</h1>');
    expect(html).toContain('<strong>tăng mạnh</strong>');
    expect(html).toContain('<li>Hà Nội</li>');
    expect(html).toContain('<table>');
    expect(html).toContain('Tháng');
  });
});

describe('nhận diện tệp Office', () => {
  it.each([
    ['báo cáo.docx', 'application/octet-stream', 'docx', 'docx'],
    ['số liệu.xlsx', null, 'sheet', 'sheet'],
    ['macro.xlsm', null, 'sheet', null],
    ['danh sách.csv', 'text/csv', 'csv', 'csv'],
    ['ghi chú.md', 'text/markdown', 'text', 'text'],
    ['cũ.doc', 'application/msword', 'none', null],
    ['cũ.xls', 'application/vnd.ms-excel', 'sheet', 'sheet'],
    ['slide.pptx', null, 'none', null],
    ['ảnh.png', 'image/png', 'image', null],
  ])('%s ⇒ xem %s, sửa %s', (name, mime, view, edit) => {
    expect(previewKind(file(name, mime))).toBe(view);
    expect(editKind(file(name, mime))).toBe(edit);
  });
});

describe('đọc / ghi bảng tính nhiều định dạng', () => {
  async function prefixedXlsx(): Promise<ArrayBuffer> {
    // Giống tệp do OpenXML SDK (.NET) tạo: phần tử SpreadsheetML có tiền tố "x:" và không ghi địa chỉ ô.
    const wb = new Workbook();
    wb.addWorksheet('Danh sách').addRow(['Họ tên', 'Ngày tiêm']);
    wb.getWorksheet('Danh sách')!.addRow(['Nguyễn An', 3]);
    const zip = await JSZip.loadAsync(await wb.xlsx.writeBuffer());
    const main = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
    for (const name of Object.keys(zip.files).filter((n) => n.startsWith('xl/') && n.endsWith('.xml'))) {
      const xml = await zip.file(name)!.async('string');
      if (!xml.includes(`xmlns="${main}"`)) continue;
      zip.file(
        name,
        xml
          .replace(/<(\/?)([A-Za-z])/g, (_, slash: string, c: string) => `<${slash}x:${c}`)
          .replace(/<(\/?)x:\?/g, '<$1?')
          .replace(`xmlns="${main}"`, `xmlns:x="${main}"`)
          // ... và bỏ địa chỉ ô (<c r="A1">), Excel tự suy theo vị trí.
          .replace(/<x:c r="[A-Z]+[0-9]+"/g, '<x:c'),
      );
    }
    return zip.generateAsync({ type: 'arraybuffer' });
  }

  it('xlsx có tiền tố namespace (OpenXML SDK) được chuẩn hoá trước khi đọc', async () => {
    const bytes = await prefixedXlsx();
    await expect(new Workbook().xlsx.load(bytes)).rejects.toThrow(); // ExcelJS đọc thẳng thì hỏng

    const { workbook, format } = await loadWorkbook(bytes);
    expect(format).toBe('xlsx');
    const source = worksheetSource(workbook.getWorksheet('Danh sách')!);
    expect(source.display(2, 1)).toBe('Nguyễn An');
  });

  it('.xls nhị phân cũ: đọc, sửa, lưu lại vẫn là .xls', async () => {
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(
      book,
      XLSX.utils.aoa_to_sheet([
        ['Tháng', 'Số'],
        [1, 10],
        [2, 20],
      ]),
      'Trang 1',
    );
    const xls = XLSX.write(book, { bookType: 'xls', type: 'array' }) as ArrayBuffer;
    expect(new Uint8Array(xls).slice(0, 4)).toEqual(new Uint8Array([0xd0, 0xcf, 0x11, 0xe0]));

    const loaded = await loadWorkbook(xls);
    expect(loaded.format).toBe('legacy');
    const source = worksheetSource(loaded.workbook.getWorksheet('Trang 1')!);
    expect(source.display(3, 2)).toBe('20');
    source.set(3, 2, '25');

    const saved = new Uint8Array(await readBlob(await saveWorkbook(loaded)));
    expect(saved.slice(0, 4)).toEqual(new Uint8Array([0xd0, 0xcf, 0x11, 0xe0]));
    const again = XLSX.read(saved, { type: 'array' });
    expect(again.Sheets['Trang 1']!.B3!.v).toBe(25);
  });

  it('".xls" thực chất là bảng HTML (nhiều hệ thống xuất như vậy) vẫn xem được', async () => {
    const html = new TextEncoder().encode(
      '<html><body><table><tr><td>Mã</td><td>Tên</td></tr><tr><td>1</td><td>Bình</td></tr></table></body></html>',
    );
    const { workbook, format } = await loadWorkbook(html.buffer);
    expect(format).toBe('legacy');
    expect(worksheetSource(workbook.worksheets[0]!).display(2, 2)).toBe('Bình');
  });

  it('nhận diện .xls (kể cả đuôi viết hoa) là bảng tính xem và sửa được', () => {
    expect(previewKind(file('BaoCao.XLS', 'application/vnd.ms-excel'))).toBe('sheet');
    expect(editKind(file('BaoCao.XLS', 'application/vnd.ms-excel'))).toBe('sheet');
    expect(editKind(file('du-lieu.xls', null))).toBe('sheet');
  });
});
