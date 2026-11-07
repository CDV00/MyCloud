import { describe, expect, it } from 'vitest';
import { MAX_TEXT_PREVIEW_BYTES, previewKind } from './previewKind';

const file = (name: string, mimeType: string | null, sizeBytes = 100) => ({
  kind: 'file' as const,
  name,
  mimeType,
  sizeBytes,
});

describe('previewKind', () => {
  it.each([
    ['a.jpg', 'image/jpeg', 'image'],
    ['a.mp4', 'video/mp4', 'video'],
    ['a.mp3', 'audio/mpeg', 'audio'],
    ['a.pdf', 'application/pdf', 'pdf'],
    ['a.txt', 'text/plain', 'text'],
    ['Program.cs', 'application/octet-stream', 'text'],
    ['Dockerfile', null, 'text'],
    // SVG có thể chứa script: chỉ xem như văn bản.
    ['logo.svg', 'image/svg+xml', 'text'],
    ['a.heic', 'image/heic', 'none'],
    ['a.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'docx'],
  ])('%s (%s) ⇒ %s', (name, mime, kind) => {
    expect(previewKind(file(name, mime))).toBe(kind);
  });

  it('văn bản quá lớn và thư mục không xem trước', () => {
    expect(previewKind(file('log.txt', 'text/plain', MAX_TEXT_PREVIEW_BYTES + 1))).toBe('none');
    expect(previewKind({ kind: 'folder', name: 'A', mimeType: null, sizeBytes: 0 })).toBe('none');
  });
});
