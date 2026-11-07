import { DOCX_MIME, XLSX_MIME } from '../preview/previewKind';

export type NewFileKind = 'docx' | 'xlsx' | 'txt' | 'md';

export const NEW_FILE_KINDS: Record<
  NewFileKind,
  { label: string; title: string; defaultName: string; mime: string }
> = {
  docx: {
    label: 'Tài liệu Word',
    title: 'Tài liệu Word mới',
    defaultName: 'Tài liệu không có tiêu đề',
    mime: DOCX_MIME,
  },
  xlsx: {
    label: 'Bảng tính Excel',
    title: 'Bảng tính Excel mới',
    defaultName: 'Bảng tính không có tiêu đề',
    mime: XLSX_MIME,
  },
  txt: { label: 'Tệp văn bản', title: 'Tệp văn bản mới', defaultName: 'Ghi chú', mime: 'text/plain' },
  md: {
    label: 'Tài liệu Markdown',
    title: 'Tài liệu Markdown mới',
    defaultName: 'Ghi chú',
    mime: 'text/markdown',
  },
};
