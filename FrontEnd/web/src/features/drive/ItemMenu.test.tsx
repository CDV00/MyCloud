import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { DriveItem, Role } from '../../api/drive';
import { ItemMenu } from './ItemMenu';
import type { ItemActions } from './useItemActions';

function file(role: Role): DriveItem {
  return {
    id: 'f1',
    parentId: 'p1',
    kind: 'file',
    name: 'báo cáo.pdf',
    mimeType: 'application/pdf',
    sizeBytes: 10,
    isRoot: false,
    revision: 1,
    createdAt: '2026-11-07T00:00:00Z',
    modifiedAt: '2026-11-07T00:00:00Z',
    starred: false,
    trashedAt: null,
    versionNo: 1,
    ownerId: 'owner',
    modifiedBy: null,
    role,
    hasThumbnail: false,
  };
}

const actions = new Proxy({}, { get: () => vi.fn() }) as unknown as ItemActions;

function menuLabels(role: Role) {
  const item = file(role);
  render(
    <ItemMenu
      state={{ item, anchor: { top: 0, left: 0 } }}
      targets={[item]}
      actions={actions}
      onClose={() => undefined}
    />,
  );
  return screen.getAllByRole('menuitem').map((m) => m.textContent);
}

describe('ItemMenu theo vai trò', () => {
  it('chủ sở hữu thấy mọi thao tác, kể cả chuyển vào thùng rác', () => {
    expect(menuLabels('owner')).toEqual([
      'Xem trước',
      'Tải xuống',
      'Chia sẻ',
      'Đổi tên',
      'Tạo bản sao',
      'Di chuyển',
      'Gắn dấu sao',
      'Thông tin chi tiết',
      'Lịch sử & trao đổi',
      'Tải lên phiên bản mới',
      'Chuyển vào thùng rác',
    ]);
  });

  it('người chỉnh sửa sửa được nhưng không xoá được', () => {
    const labels = menuLabels('editor');
    expect(labels).toContain('Đổi tên');
    expect(labels).toContain('Tải lên phiên bản mới');
    expect(labels).not.toContain('Chuyển vào thùng rác');
  });

  it('người xem chỉ mở, xem quyền chia sẻ, sao chép, gắn sao, xem thông tin', () => {
    expect(menuLabels('viewer')).toEqual([
      'Xem trước',
      'Tải xuống',
      'Chia sẻ',
      'Tạo bản sao',
      'Gắn dấu sao',
      'Thông tin chi tiết',
      'Lịch sử & trao đổi',
    ]);
  });
});
