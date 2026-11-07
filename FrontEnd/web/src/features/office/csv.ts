/** CSV theo RFC 4180: trường có dấu ngoặc kép, xuống dòng trong trường, "" là một dấu ngoặc kép. */

/** Đoán dấu phân cách từ dòng đầu (Excel bản tiếng Việt / châu Âu hay xuất bằng ";"). */
export function detectDelimiter(text: string, fileName = ''): string {
  if (fileName.toLowerCase().endsWith('.tsv')) return '\t';
  const firstLine = text.slice(0, text.indexOf('\n') >= 0 ? text.indexOf('\n') : text.length);
  const counts = [',', ';', '\t'].map((d) => [d, firstLine.split(d).length - 1] as const);
  return counts.sort((a, b) => b[1] - a[1])[0]![1] > 0 ? counts[0]![0] : ',';
}

export function parseCsv(text: string, delimiter = ','): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  const input = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text; // bỏ BOM

  for (let i = 0; i < input.length; i++) {
    const c = input[i]!;
    if (quoted) {
      if (c === '"') {
        if (input[i + 1] === '"') {
          field += '"';
          i++;
        } else {
          quoted = false;
        }
      } else {
        field += c;
      }
    } else if (c === '"' && field === '') {
      quoted = true;
    } else if (c === delimiter) {
      row.push(field);
      field = '';
    } else if (c === '\n' || c === '\r') {
      if (c === '\r' && input[i + 1] === '\n') i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else {
      field += c;
    }
  }

  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }

  return rows;
}

export function toCsv(rows: string[][], delimiter = ','): string {
  const quote = (value: string) =>
    value.includes(delimiter) || value.includes('"') || value.includes('\n') || value.includes('\r')
      ? `"${value.replaceAll('"', '""')}"`
      : value;
  // Bỏ các ô trống ở cuối mỗi dòng và các dòng trống ở cuối tệp (lưới hiển thị thừa ra để gõ thêm).
  const trimmed = rows.map((r) => {
    let end = r.length;
    while (end > 0 && r[end - 1] === '') end--;
    return r.slice(0, end);
  });
  let last = trimmed.length;
  while (last > 0 && trimmed[last - 1]!.length === 0) last--;
  return trimmed
    .slice(0, last)
    .map((r) => r.map(quote).join(delimiter))
    .join('\r\n');
}
