import { describe, expect, it } from 'vitest';
import { formatSize, initials } from '../lib/format';
import { toUserMessage } from './errors';
import { ApiError } from './http';

describe('toUserMessage', () => {
  it('map mã lỗi đã biết sang tiếng Việt', () => {
    expect(toUserMessage(new ApiError(409, { code: 'IDENTITY.EMAIL_TAKEN' }))).toBe(
      'Email này đã được đăng ký.',
    );
  });

  it('429 ⇒ thông báo thao tác quá nhanh', () => {
    expect(toUserMessage(new ApiError(429, {}))).toMatch(/quá nhanh/);
  });

  it('400 có danh sách lỗi ⇒ ghép các thông báo', () => {
    const error = new ApiError(400, { errors: [{ code: 'Name', message: 'Tên bắt buộc.' }] });
    expect(toUserMessage(error)).toBe('Tên bắt buộc.');
  });

  it('lỗi không phải ApiError ⇒ lỗi kết nối', () => {
    expect(toUserMessage(new TypeError('Failed to fetch'))).toBe('Không kết nối được tới máy chủ.');
  });
});

describe('formatSize', () => {
  it.each([
    [0, '0 B'],
    [1023, '1023 B'],
    [1536, '1,5 KB'],
    [5 * 1024 * 1024, '5 MB'],
  ])('%d bytes ⇒ %s', (bytes, expected) => {
    expect(formatSize(bytes)).toBe(expected);
  });
});

describe('initials', () => {
  it.each([
    ['Nguyễn Văn An', 'NA'],
    ['minh', 'M'],
    ['   ', '?'],
  ])('%s ⇒ %s', (name, expected) => {
    expect(initials(name)).toBe(expected);
  });
});
