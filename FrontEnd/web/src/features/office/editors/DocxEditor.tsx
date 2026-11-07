import FormatBoldIcon from '@mui/icons-material/FormatBold';
import FormatItalicIcon from '@mui/icons-material/FormatItalic';
import FormatListBulletedIcon from '@mui/icons-material/FormatListBulleted';
import FormatListNumberedIcon from '@mui/icons-material/FormatListNumbered';
import FormatQuoteIcon from '@mui/icons-material/FormatQuote';
import FormatStrikethroughIcon from '@mui/icons-material/FormatStrikethrough';
import FormatUnderlinedIcon from '@mui/icons-material/FormatUnderlined';
import HorizontalRuleIcon from '@mui/icons-material/HorizontalRule';
import RedoIcon from '@mui/icons-material/Redo';
import TableChartOutlinedIcon from '@mui/icons-material/TableChartOutlined';
import UndoIcon from '@mui/icons-material/Undo';
import {
  Alert,
  Box,
  CircularProgress,
  Divider,
  IconButton,
  Select,
  Stack,
  Tooltip,
  Button,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Image } from '@tiptap/extension-image';
import { TableKit } from '@tiptap/extension-table';
import { EditorContent, useEditor, useEditorState, type Editor } from '@tiptap/react';
import { StarterKit } from '@tiptap/starter-kit';
import mammoth from 'mammoth';
import { useEffect, type ReactNode } from 'react';
import { toDocx } from '../docxExport';
import type { EditorProps } from './types';

/**
 * Word (.docx): mammoth chuyển tài liệu thành HTML có cấu trúc (tiêu đề, danh sách, bảng, ảnh, liên kết) ⇒ soạn bằng
 * TipTap ⇒ lưu lại thành .docx (docxExport). Bố cục trang, đầu / chân trang, font, màu, cột… không được giữ — mỗi lần
 * lưu là một phiên bản mới nên bản gốc vẫn còn trong lịch sử phiên bản (ADR-016).
 */
export default function DocxEditor({ bytes, onDirty, register }: EditorProps) {
  const html = useQuery({
    queryKey: ['docx-html', bytes],
    staleTime: Infinity,
    gcTime: 0,
    queryFn: async () => (await mammoth.convertToHtml({ arrayBuffer: bytes })).value,
  });

  if (html.error)
    return (
      <Alert severity="error">
        Không mở được tài liệu Word này (có thể tệp bị hỏng hoặc là định dạng .doc cũ).
      </Alert>
    );
  if (html.data === undefined) return <CircularProgress sx={{ m: 'auto' }} />;
  return <RichEditor html={html.data} onDirty={onDirty} register={register} />;
}

function RichEditor({
  html,
  onDirty,
  register,
}: { html: string } & Pick<EditorProps, 'onDirty' | 'register'>) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({ link: { openOnClick: false, autolink: true } }),
      TableKit.configure({ table: { resizable: false } }),
      Image.configure({ allowBase64: true, inline: false }),
    ],
    content: html || '<p></p>',
    onUpdate: () => onDirty(),
    editorProps: { attributes: { 'aria-label': 'Nội dung tài liệu', spellcheck: 'true' } },
  });

  useEffect(() => {
    if (editor) register(() => toDocx(editor.getJSON()));
  }, [editor, register]);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <Toolbar editor={editor} />
      <Alert severity="info" sx={{ borderRadius: 0, py: 0 }}>
        Trình sửa giữ tiêu đề, định dạng chữ, danh sách, bảng, ảnh và liên kết. Bố cục trang, đầu / chân
        trang, font và màu không được giữ — bản trước vẫn còn trong “Lịch sử & trao đổi”.
      </Alert>
      <Box sx={{ flexGrow: 1, overflow: 'auto', bgcolor: 'background.default', py: 3 }}>
        <Box
          sx={{
            maxWidth: 816, // khổ A4 ở 96 dpi
            mx: 'auto',
            minHeight: '100%',
            bgcolor: '#fff',
            color: '#202124',
            boxShadow: '0 1px 4px rgba(0,0,0,.25)',
            px: { xs: 2, sm: 9 },
            py: 6,
            '& .tiptap': {
              outline: 'none',
              minHeight: 600,
              fontFamily: 'Calibri, Arial, sans-serif',
              fontSize: 15,
              lineHeight: 1.6,
            },
            '& .tiptap h1': { fontSize: 28, fontWeight: 600, mt: 3, mb: 1.5, lineHeight: 1.25 },
            '& .tiptap h2': { fontSize: 22, fontWeight: 600, mt: 2.5, mb: 1, lineHeight: 1.3 },
            '& .tiptap h3': { fontSize: 18, fontWeight: 600, mt: 2, mb: 1 },
            '& .tiptap p': { my: 0.75 },
            '& .tiptap table': { borderCollapse: 'collapse', width: '100%', my: 1 },
            '& .tiptap td, & .tiptap th': {
              border: '1px solid #bbb',
              p: '4px 8px',
              verticalAlign: 'top',
              minWidth: 40,
            },
            '& .tiptap th': { bgcolor: '#f1f3f4', fontWeight: 600 },
            '& .tiptap .selectedCell': { bgcolor: 'rgba(11,87,208,.12)' },
            '& .tiptap img': { maxWidth: '100%', height: 'auto' },
            '& .tiptap blockquote': { borderLeft: '3px solid #bbb', pl: 2, ml: 0, color: '#555' },
            '& .tiptap pre': {
              bgcolor: '#f1f3f4',
              p: 1.5,
              borderRadius: 1,
              fontFamily: 'Consolas, monospace',
            },
            '& .tiptap a': { color: '#0b57d0' },
          }}
        >
          <EditorContent editor={editor} />
        </Box>
      </Box>
    </Box>
  );
}

function Toolbar({ editor }: { editor: Editor | null }) {
  const state = useEditorState({
    editor,
    selector: ({ editor: e }) =>
      e
        ? {
            heading: [1, 2, 3].find((l) => e.isActive('heading', { level: l })) ?? 0,
            bold: e.isActive('bold'),
            italic: e.isActive('italic'),
            underline: e.isActive('underline'),
            strike: e.isActive('strike'),
            bullet: e.isActive('bulletList'),
            ordered: e.isActive('orderedList'),
            quote: e.isActive('blockquote'),
            table: e.isActive('table'),
            canUndo: e.can().undo(),
            canRedo: e.can().redo(),
          }
        : null,
  });
  if (!editor || !state) return null;
  const chain = () => editor.chain().focus();

  const button = (title: string, active: boolean, onClick: () => void, icon: ReactNode, disabled = false) => (
    <Tooltip title={title}>
      <span>
        <IconButton
          size="small"
          aria-label={title}
          aria-pressed={active}
          disabled={disabled}
          onClick={onClick}
          color={active ? 'primary' : 'default'}
          sx={{ bgcolor: active ? 'action.selected' : undefined }}
        >
          {icon}
        </IconButton>
      </span>
    </Tooltip>
  );

  return (
    <Stack
      direction="row"
      spacing={0.5}
      useFlexGap
      sx={{
        alignItems: 'center',
        flexWrap: 'wrap',
        px: 1,
        py: 0.5,
        borderBottom: 1,
        borderColor: 'divider',
        bgcolor: 'background.paper',
      }}
    >
      {button(
        'Hoàn tác (Ctrl+Z)',
        false,
        () => chain().undo().run(),
        <UndoIcon fontSize="small" />,
        !state.canUndo,
      )}
      {button(
        'Làm lại (Ctrl+Y)',
        false,
        () => chain().redo().run(),
        <RedoIcon fontSize="small" />,
        !state.canRedo,
      )}
      <Divider orientation="vertical" flexItem />
      {/* Ô chọn native: không mở thêm popover trong hộp thoại toàn màn hình (tránh tranh focus với trình soạn thảo). */}
      <Select
        native
        size="small"
        value={state.heading}
        onChange={(e) => {
          const level = Number(e.target.value);
          if (level === 0) chain().setParagraph().run();
          else
            chain()
              .setHeading({ level: level as 1 | 2 | 3 })
              .run();
        }}
        inputProps={{ 'aria-label': 'Kiểu đoạn' }}
        sx={{ minWidth: 150, '& .MuiNativeSelect-select': { py: 0.5 } }}
      >
        <option value={0}>Văn bản thường</option>
        <option value={1}>Tiêu đề 1</option>
        <option value={2}>Tiêu đề 2</option>
        <option value={3}>Tiêu đề 3</option>
      </Select>
      <Divider orientation="vertical" flexItem />
      {button(
        'Đậm (Ctrl+B)',
        state.bold,
        () => chain().toggleBold().run(),
        <FormatBoldIcon fontSize="small" />,
      )}
      {button(
        'Nghiêng (Ctrl+I)',
        state.italic,
        () => chain().toggleItalic().run(),
        <FormatItalicIcon fontSize="small" />,
      )}
      {button(
        'Gạch chân (Ctrl+U)',
        state.underline,
        () => chain().toggleUnderline().run(),
        <FormatUnderlinedIcon fontSize="small" />,
      )}
      {button(
        'Gạch ngang',
        state.strike,
        () => chain().toggleStrike().run(),
        <FormatStrikethroughIcon fontSize="small" />,
      )}
      <Divider orientation="vertical" flexItem />
      {button(
        'Danh sách dấu đầu dòng',
        state.bullet,
        () => chain().toggleBulletList().run(),
        <FormatListBulletedIcon fontSize="small" />,
      )}
      {button(
        'Danh sách đánh số',
        state.ordered,
        () => chain().toggleOrderedList().run(),
        <FormatListNumberedIcon fontSize="small" />,
      )}
      {button(
        'Trích dẫn',
        state.quote,
        () => chain().toggleBlockquote().run(),
        <FormatQuoteIcon fontSize="small" />,
      )}
      {button(
        'Đường kẻ ngang',
        false,
        () => chain().setHorizontalRule().run(),
        <HorizontalRuleIcon fontSize="small" />,
      )}
      {button(
        'Chèn bảng 3 × 3',
        false,
        () => chain().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
        <TableChartOutlinedIcon fontSize="small" />,
      )}
      {state.table && (
        <>
          <Divider orientation="vertical" flexItem />
          <Button size="small" onClick={() => chain().addRowAfter().run()}>
            + Hàng
          </Button>
          <Button size="small" onClick={() => chain().addColumnAfter().run()}>
            + Cột
          </Button>
          <Button size="small" onClick={() => chain().deleteRow().run()}>
            − Hàng
          </Button>
          <Button size="small" onClick={() => chain().deleteColumn().run()}>
            − Cột
          </Button>
          <Button size="small" color="error" onClick={() => chain().deleteTable().run()}>
            Xoá bảng
          </Button>
        </>
      )}
    </Stack>
  );
}
