import type { DriveItem } from '../../api/drive';

export type PreviewKind = 'image' | 'video' | 'audio' | 'pdf' | 'text' | 'docx' | 'sheet' | 'csv' | 'none';

/** Cách mở trong trình sửa (người có quyền sửa); null = không sửa được trong trình duyệt. */
export type EditKind = 'text' | 'csv' | 'sheet' | 'docx';

/** Word / Excel lớn hơn ⇒ không xem / sửa trong trình duyệt (giải nén + dựng cả tài liệu trong bộ nhớ). */
export const MAX_OFFICE_BYTES = 20 * 1024 * 1024;

export const DOCX_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';

/** Văn bản lớn hơn ⇒ không xem trước (tải cả file về trình duyệt). */
export const MAX_TEXT_PREVIEW_BYTES = 2 * 1024 * 1024;

// Ảnh trình duyệt hiển thị được. SVG xem như văn bản: Storage không bao giờ trả SVG để hiển thị (có thể chứa script).
const IMAGE_TYPES = new Set([
  'image/jpeg',
  'image/png',
  'image/gif',
  'image/webp',
  'image/bmp',
  'image/avif',
  'image/x-icon',
  'image/vnd.microsoft.icon',
]);

const TEXT_TYPES = new Set([
  'application/json',
  'application/xml',
  'application/javascript',
  'application/x-javascript',
  'application/x-sh',
  'application/x-yaml',
  'application/yaml',
  'application/sql',
  'application/toml',
  'application/x-ndjson',
  'image/svg+xml',
]);

const TEXT_EXTENSIONS = new Set(
  (
    'txt md markdown csv tsv log json jsonc xml yaml yml toml ini cfg conf env properties sql sh bash ps1 bat ' +
    'js mjs cjs jsx ts tsx css scss less html htm vue svelte py rb go rs java kt kts cs fs vb c h cpp hpp cc ' +
    'php pl swift dart lua r m scala clj ex exs erl hs gradle dockerfile makefile proto graphql gql svg tex'
  ).split(' '),
);

export function extension(name: string) {
  const lower = name.toLowerCase();
  if (lower === 'dockerfile' || lower === 'makefile') return lower;
  const dot = lower.lastIndexOf('.');
  return dot >= 0 ? lower.slice(dot + 1) : '';
}

/** Cách xem trước một file (theo MIME, rồi theo phần mở rộng cho file văn bản / mã nguồn). */
export function previewKind(item: Pick<DriveItem, 'kind' | 'name' | 'mimeType' | 'sizeBytes'>): PreviewKind {
  if (item.kind !== 'file') return 'none';
  const mime = (item.mimeType ?? '').split(';')[0]!.trim().toLowerCase();
  if (IMAGE_TYPES.has(mime)) return 'image';
  if (mime.startsWith('video/')) return 'video';
  if (mime.startsWith('audio/')) return 'audio';
  if (mime === 'application/pdf') return 'pdf';
  const ext = extension(item.name);
  if (mime === DOCX_MIME || ext === 'docx') return item.sizeBytes <= MAX_OFFICE_BYTES ? 'docx' : 'none';
  // Bảng tính: xlsx / xlsm và .xls (nhị phân cũ, HTML, XML 2003 — đọc bằng SheetJS). .xlsm chỉ xem (ghi lại mất macro).
  // .doc (Word nhị phân cũ) không đọc được trong trình duyệt.
  if (
    mime === XLSX_MIME ||
    mime === 'application/vnd.ms-excel' ||
    mime === 'application/vnd.ms-excel.sheet.macroenabled.12' ||
    ext === 'xlsx' ||
    ext === 'xlsm' ||
    ext === 'xls'
  ) {
    return item.sizeBytes <= MAX_OFFICE_BYTES ? 'sheet' : 'none';
  }
  if (mime === 'text/csv' || ext === 'csv' || ext === 'tsv') {
    return item.sizeBytes <= MAX_TEXT_PREVIEW_BYTES ? 'csv' : 'none';
  }
  const textual =
    mime.startsWith('text/') || TEXT_TYPES.has(mime) || TEXT_EXTENSIONS.has(extension(item.name));
  if (textual) return item.sizeBytes <= MAX_TEXT_PREVIEW_BYTES ? 'text' : 'none';
  return 'none';
}

/** File sửa được trong trình duyệt (cùng giới hạn kích thước với xem trước). */
export function editKind(item: Pick<DriveItem, 'kind' | 'name' | 'mimeType' | 'sizeBytes'>): EditKind | null {
  const kind = previewKind(item);
  if (kind === 'text' || kind === 'csv' || kind === 'docx') return kind;
  if (kind === 'sheet') return extension(item.name) === 'xlsm' ? null : 'sheet';
  return null;
}
