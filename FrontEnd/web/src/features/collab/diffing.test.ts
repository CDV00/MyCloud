import { Workbook } from 'exceljs';
import { describe, expect, it } from 'vitest';
import { compareText, compareWorkbooks } from './diffing';

describe('so sánh văn bản', () => {
  it('đếm dòng thêm / bớt và thu gọn đoạn dài không đổi, giữ 3 dòng ngữ cảnh', () => {
    const before = Array.from({ length: 20 }, (_, i) => `dòng ${i + 1}`).join('\n');
    const after = before.replace('dòng 10', 'dòng mười').replace('dòng 20', 'dòng 20\ndòng 21');

    const diff = compareText(before, after);

    expect(diff.added).toBe(2);
    expect(diff.removed).toBe(1);
    expect(diff.blocks[0]).toEqual({ collapsed: 6 }); // dòng 1–6 ẩn, 7–9 là ngữ cảnh
    const shown = diff.blocks.flatMap((b) => ('lines' in b ? b.lines : []));
    expect(shown).toContainEqual({ kind: 'removed', text: 'dòng 10' });
    expect(shown).toContainEqual({ kind: 'added', text: 'dòng mười' });
    expect(shown).toContainEqual({ kind: 'added', text: 'dòng 21' });
    expect(shown.filter((l) => l.kind === 'same').map((l) => l.text)).toContain('dòng 9');
  });

  it('không đổi (kể cả khác kiểu xuống dòng CRLF / LF) ⇒ không có thay đổi', () => {
    const diff = compareText('a\r\nb\r\n', 'a\nb\n');
    expect([diff.added, diff.removed]).toEqual([0, 0]);
  });
});

describe('so sánh bảng tính', () => {
  it('liệt kê ô đổi giá trị / công thức và trang tính thêm / bớt', () => {
    const before = new Workbook();
    const s1 = before.addWorksheet('Doanh thu');
    s1.getCell('A1').value = 'Tháng';
    s1.getCell('B2').value = 120;
    s1.getCell('B3').value = { formula: 'B2*2', result: 240 } as never;
    before.addWorksheet('Cũ');

    const after = new Workbook();
    const s2 = after.addWorksheet('Doanh thu');
    s2.getCell('A1').value = 'Tháng';
    s2.getCell('B2').value = 150;
    s2.getCell('B3').value = { formula: 'B2*3', result: 450 } as never;
    s2.getCell('C1').value = 'Ghi chú';
    after.addWorksheet('Mới');

    const diff = compareWorkbooks(before, after);

    expect(diff.changes).toEqual([
      { sheet: 'Doanh thu', cell: 'C1', before: '', after: 'Ghi chú' },
      { sheet: 'Doanh thu', cell: 'B2', before: '120', after: '150' },
      { sheet: 'Doanh thu', cell: 'B3', before: '=B2*2', after: '=B2*3' },
    ]);
    expect(diff.addedSheets).toEqual(['Mới']);
    expect(diff.removedSheets).toEqual(['Cũ']);
  });
});
