import type { JSONContent } from '@tiptap/react';
import {
  AlignmentType,
  BorderStyle,
  Document,
  ExternalHyperlink,
  HeadingLevel,
  ImageRun,
  LevelFormat,
  Packer,
  Paragraph,
  Table,
  TableCell,
  TableRow,
  TextRun,
  WidthType,
  type ParagraphChild,
} from 'docx';

const HEADINGS = [
  HeadingLevel.HEADING_1,
  HeadingLevel.HEADING_2,
  HeadingLevel.HEADING_3,
  HeadingLevel.HEADING_4,
  HeadingLevel.HEADING_5,
  HeadingLevel.HEADING_6,
] as const;

const MAX_IMAGE_WIDTH = 600; // px (~16 cm)

type Block = Paragraph | Table;

interface Context {
  /** Cấp lồng của danh sách (0 = không trong danh sách). */
  listLevel: number;
  ordered: boolean;
  /** Mỗi danh sách đánh số có một "instance" riêng để đánh số lại từ 1. */
  instance: number;
  quote: boolean;
  images: Map<
    string,
    { data: Uint8Array; type: 'png' | 'jpg' | 'gif' | 'bmp'; width: number; height: number }
  >;
  nextInstance(): number;
}

/** Ảnh data URI (mammoth nhúng ảnh của tài liệu dạng base64) ⇒ byte + kích thước, để đưa lại vào docx. */
async function loadImages(doc: JSONContent): Promise<Context['images']> {
  const result: Context['images'] = new Map();
  const sources: string[] = [];
  const walk = (node: JSONContent) => {
    if (
      node.type === 'image' &&
      typeof node.attrs?.src === 'string' &&
      node.attrs.src.startsWith('data:image/')
    ) {
      sources.push(node.attrs.src);
    }

    node.content?.forEach(walk);
  };
  walk(doc);

  for (const src of new Set(sources)) {
    const match = /^data:image\/(png|jpe?g|gif|bmp);base64,(.+)$/i.exec(src);
    if (!match) continue; // định dạng ảnh khác (vd SVG, EMF) không đưa vào được
    const binary = atob(match[2]!);
    const data = Uint8Array.from(binary, (c) => c.charCodeAt(0));
    const size = await new Promise<{ width: number; height: number }>((resolve) => {
      const img = new Image();
      img.onload = () => resolve({ width: img.naturalWidth || 300, height: img.naturalHeight || 200 });
      img.onerror = () => resolve({ width: 300, height: 200 });
      img.src = src;
    });
    const scale = Math.min(1, MAX_IMAGE_WIDTH / size.width);
    const kind = match[1]!.toLowerCase();
    result.set(src, {
      data,
      type: kind === 'jpeg' || kind === 'jpg' ? 'jpg' : (kind as 'png' | 'gif' | 'bmp'),
      width: Math.round(size.width * scale),
      height: Math.round(size.height * scale),
    });
  }

  return result;
}

function runs(nodes: JSONContent[] | undefined, ctx: Context): ParagraphChild[] {
  const children: ParagraphChild[] = [];
  for (const node of nodes ?? []) {
    if (node.type === 'hardBreak') {
      children.push(new TextRun({ text: '', break: 1 }));
      continue;
    }

    if (node.type === 'image') {
      const image = ctx.images.get(String(node.attrs?.src ?? ''));
      if (image) {
        children.push(
          new ImageRun({
            type: image.type,
            data: image.data,
            transformation: { width: image.width, height: image.height },
            altText: node.attrs?.alt
              ? { name: String(node.attrs.alt), description: String(node.attrs.alt) }
              : undefined,
          }),
        );
      }

      continue;
    }

    if (node.type !== 'text' || !node.text) continue;
    const marks = new Set((node.marks ?? []).map((m) => m.type));
    const link = node.marks?.find((m) => m.type === 'link')?.attrs?.href as string | undefined;
    const run = new TextRun({
      text: node.text,
      bold: marks.has('bold'),
      italics: marks.has('italic') || ctx.quote,
      underline: marks.has('underline') ? {} : undefined,
      strike: marks.has('strike'),
      font: marks.has('code') ? 'Consolas' : undefined,
      style: link ? 'Hyperlink' : undefined,
    });
    children.push(
      link && /^(https?:|mailto:)/i.test(link) ? new ExternalHyperlink({ link, children: [run] }) : run,
    );
  }

  return children;
}

function paragraph(
  node: JSONContent,
  ctx: Context,
  extra: { heading?: (typeof HEADINGS)[number] } = {},
): Paragraph {
  const align = node.attrs?.textAlign as string | undefined;
  return new Paragraph({
    children: runs(node.content, ctx),
    heading: extra.heading,
    alignment:
      align === 'center'
        ? AlignmentType.CENTER
        : align === 'right'
          ? AlignmentType.RIGHT
          : align === 'justify'
            ? AlignmentType.JUSTIFIED
            : undefined,
    ...(ctx.listLevel > 0
      ? ctx.ordered
        ? { numbering: { reference: 'ordered', level: ctx.listLevel - 1, instance: ctx.instance } }
        : { bullet: { level: ctx.listLevel - 1 } }
      : {}),
    ...(ctx.quote
      ? {
          indent: { left: 720 },
          border: { left: { style: BorderStyle.SINGLE, size: 12, color: 'BBBBBB', space: 8 } },
        }
      : {}),
  });
}

function blocks(nodes: JSONContent[] | undefined, ctx: Context): Block[] {
  const out: Block[] = [];
  for (const node of nodes ?? []) {
    switch (node.type) {
      case 'paragraph':
        out.push(paragraph(node, ctx));
        break;
      case 'heading':
        out.push(
          paragraph(node, ctx, {
            heading: HEADINGS[Math.min(6, Math.max(1, Number(node.attrs?.level ?? 1))) - 1],
          }),
        );
        break;
      case 'bulletList':
      case 'orderedList': {
        const ordered = node.type === 'orderedList';
        const inner: Context = {
          ...ctx,
          listLevel: Math.min(9, ctx.listLevel + 1),
          ordered,
          instance: ordered && !(ctx.ordered && ctx.listLevel > 0) ? ctx.nextInstance() : ctx.instance,
        };
        for (const item of node.content ?? []) out.push(...blocks(item.content, inner));
        break;
      }
      case 'blockquote':
        out.push(...blocks(node.content, { ...ctx, quote: true }));
        break;
      case 'codeBlock': {
        const text = (node.content ?? []).map((t) => t.text ?? '').join('');
        out.push(
          new Paragraph({
            shading: { fill: 'F1F3F4' },
            children: text
              .split('\n')
              .map((line, i) => new TextRun({ text: line, font: 'Consolas', break: i > 0 ? 1 : 0 })),
          }),
        );
        break;
      }
      case 'horizontalRule':
        out.push(
          new Paragraph({
            border: { bottom: { style: BorderStyle.SINGLE, size: 6, color: 'AAAAAA', space: 1 } },
          }),
        );
        break;
      case 'image':
        out.push(new Paragraph({ children: runs([node], ctx) }));
        break;
      case 'table': {
        const rows = (node.content ?? []).map(
          (row) =>
            new TableRow({
              tableHeader: (row.content ?? []).every((c) => c.type === 'tableHeader'),
              children: (row.content ?? []).map((cell) => {
                const content = blocks(cell.content, { ...ctx, listLevel: 0 });
                return new TableCell({
                  columnSpan: Number(cell.attrs?.colspan ?? 1),
                  rowSpan: Number(cell.attrs?.rowspan ?? 1),
                  shading: cell.type === 'tableHeader' ? { fill: 'F1F3F4' } : undefined,
                  // Ô bảng phải có ít nhất một đoạn văn.
                  children: content.length > 0 ? content : [new Paragraph('')],
                });
              }),
            }),
        );
        if (rows.length > 0) out.push(new Table({ rows, width: { size: 100, type: WidthType.PERCENTAGE } }));
        break;
      }
      default:
        if (node.content) out.push(...blocks(node.content, ctx));
    }
  }

  return out;
}

/**
 * Nội dung trình soạn thảo (TipTap JSON) ⇒ tệp .docx: đoạn văn, tiêu đề, đậm / nghiêng / gạch chân / gạch ngang, liên
 * kết, danh sách (lồng nhau, đánh số), trích dẫn, khối mã, đường kẻ, bảng (gộp ô), ảnh PNG / JPEG / GIF / BMP.
 */
export async function toDocx(doc: JSONContent): Promise<Blob> {
  let instances = 0;
  const ctx: Context = {
    listLevel: 0,
    ordered: false,
    instance: 0,
    quote: false,
    images: await loadImages(doc),
    nextInstance: () => ++instances,
  };
  const children = blocks(doc.content, ctx);

  const document = new Document({
    creator: 'MyCloud',
    styles: { default: { document: { run: { font: 'Calibri', size: 22 } } } },
    numbering: {
      config: [
        {
          reference: 'ordered',
          levels: Array.from({ length: 9 }, (_, level) => ({
            level,
            format: [LevelFormat.DECIMAL, LevelFormat.LOWER_LETTER, LevelFormat.LOWER_ROMAN][level % 3]!,
            text: `%${level + 1}.`,
            alignment: AlignmentType.START,
            style: { paragraph: { indent: { left: 720 * (level + 1), hanging: 360 } } },
          })),
        },
      ],
    },
    sections: [{ children: children.length > 0 ? children : [new Paragraph('')] }],
  });

  return Packer.toBlob(document);
}

/** Tài liệu Word trống (tạo file mới). */
export const emptyDocx = () => toDocx({ type: 'doc', content: [{ type: 'paragraph' }] });
