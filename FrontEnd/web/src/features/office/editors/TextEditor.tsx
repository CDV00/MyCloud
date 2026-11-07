import { Box, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import { BOM } from '../content';
import type { EditorProps } from './types';

const hasBom = (bytes: ArrayBuffer) => {
  const b = new Uint8Array(bytes, 0, Math.min(3, bytes.byteLength));
  return b[0] === 0xef && b[1] === 0xbb && b[2] === 0xbf;
};

/** Văn bản / mã nguồn / Markdown: UTF-8, giữ nguyên BOM và kiểu xuống dòng (CRLF / LF) của tệp gốc. */
export default function TextEditor({ bytes, onDirty, register }: EditorProps) {
  const [text, setText] = useState(() => {
    const decoded = new TextDecoder('utf-8').decode(bytes);
    return decoded.startsWith(BOM) ? decoded.slice(1) : decoded;
  });
  // Kiểu xuống dòng và BOM của tệp gốc — giữ nguyên khi lưu.
  const [format] = useState(() => ({ crlf: text.includes('\r\n'), bom: hasBom(bytes) }));

  useEffect(() => {
    register(async () => {
      const body = format.crlf ? text.replace(/\r?\n/g, '\r\n') : text;
      return new Blob([format.bom ? BOM : '', body], { type: 'text/plain;charset=utf-8' });
    });
  }, [text, format, register]);

  const lines = text.split('\n').length;
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', bgcolor: 'background.paper' }}>
      <Box
        component="textarea"
        value={text}
        spellCheck={false}
        aria-label="Nội dung tệp"
        onChange={(e) => {
          setText(e.target.value);
          onDirty();
        }}
        onKeyDown={(e) => {
          // Tab chèn khoảng trắng thay vì chuyển focus.
          if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey) {
            e.preventDefault();
            const el = e.currentTarget;
            const { selectionStart: start, selectionEnd: end } = el;
            setText(`${text.slice(0, start)}  ${text.slice(end)}`);
            onDirty();
            requestAnimationFrame(() => el.setSelectionRange(start + 2, start + 2));
          }
        }}
        sx={{
          flexGrow: 1,
          border: 0,
          outline: 'none',
          resize: 'none',
          p: 2,
          fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace',
          fontSize: 14,
          lineHeight: 1.6,
          bgcolor: 'background.paper',
          color: 'text.primary',
          tabSize: 2,
        }}
      />
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ px: 2, py: 0.5, borderTop: 1, borderColor: 'divider' }}
      >
        {lines.toLocaleString('vi-VN')} dòng · {text.length.toLocaleString('vi-VN')} ký tự · UTF-8
        {format.crlf ? ' · CRLF' : ''}
      </Typography>
    </Box>
  );
}
