import { describe, expect, it, vi } from 'vitest';
import type { NameConflict } from '../../api/drive';
import {
  executeUpload,
  foldersOf,
  fromFileList,
  topLevelNames,
  UploadPlanner,
  type UploadApi,
} from './uploadPlanner';

function fileAt(path: string): File {
  const file = new File(['x'], path.split('/').pop()!);
  Object.defineProperty(file, 'webkitRelativePath', { value: path.includes('/') ? path : '' });
  return file;
}

function fakeApi(merged: string[] = [], inside: NameConflict[] = []) {
  const added: { parentId: string; names: string[] }[] = [];
  const versions: { name: string; target: string; parentId: string | null }[] = [];
  const api: UploadApi = {
    findConflicts: vi.fn(() => Promise.resolve(inside)),
    createTree: vi.fn((_parent: string, paths: string[]) =>
      Promise.resolve({ folders: Object.fromEntries(paths.map((p) => [p, `id:${p}`])), merged }),
    ),
    add: (files, parentId) => added.push({ parentId, names: files.map((f) => f.name) }),
    addVersion: (file, target) =>
      versions.push({ name: file.name, target: target.id, parentId: target.parentId }),
  };
  return { api, added, versions };
}

const conflict = (
  name: string,
  kind: 'file' | 'folder',
  role: NameConflict['role'] = 'owner',
): NameConflict => ({
  name,
  kind,
  role,
  itemId: `existing:${name}`,
});

describe('uploadPlanner', () => {
  it('suy ra mọi thư mục cha và tên cấp cao nhất từ đường dẫn', () => {
    const picked = fromFileList([fileAt('Ảnh/2024/a.jpg'), fileAt('Ảnh/b.jpg'), fileAt('lẻ.txt')]);
    expect(picked.folders.sort()).toEqual(['Ảnh', 'Ảnh/2024']);
    expect(topLevelNames(picked).sort()).toEqual(['lẻ.txt', 'Ảnh']);
    expect(foldersOf(['a/b/c/d.txt'])).toEqual(['a/b/c', 'a/b', 'a']);
  });

  it('không trùng tên: tạo cây thư mục rồi đưa file vào đúng thư mục cha', async () => {
    const { api, added } = fakeApi();
    await executeUpload(
      'root',
      fromFileList([fileAt('Ảnh/2024/a.jpg'), fileAt('lẻ.txt')]),
      [],
      'keep-both',
      api,
    );

    expect(api.createTree).toHaveBeenCalledWith(
      'root',
      expect.arrayContaining(['Ảnh', 'Ảnh/2024']),
      'rename',
    );
    expect(added).toEqual(
      expect.arrayContaining([
        { parentId: 'id:Ảnh/2024', names: ['a.jpg'] },
        { parentId: 'root', names: ['lẻ.txt'] },
      ]),
    );
  });

  it('bỏ qua: mục trùng tên ở cấp cao nhất không được tải lên', async () => {
    const { api, added } = fakeApi();
    const picked = fromFileList([fileAt('Ảnh/a.jpg'), fileAt('báo cáo.pdf'), fileAt('mới.txt')]);
    await executeUpload(
      'root',
      picked,
      [conflict('BÁO CÁO.pdf', 'file'), conflict('ảnh', 'folder')],
      'skip',
      api,
    );

    expect(api.createTree).not.toHaveBeenCalled();
    expect(added).toEqual([{ parentId: 'root', names: ['mới.txt'] }]);
  });

  it('thay thế: file trùng thành phiên bản mới, thư mục được gộp và file bên trong cũng được thay', async () => {
    const { api, added, versions } = fakeApi(['Ảnh'], [conflict('a.jpg', 'file')]);
    const picked = fromFileList([fileAt('Ảnh/a.jpg'), fileAt('Ảnh/b.jpg'), fileAt('báo cáo.pdf')]);
    await executeUpload(
      'root',
      picked,
      [conflict('báo cáo.pdf', 'file'), conflict('Ảnh', 'folder')],
      'replace',
      api,
    );

    expect(api.createTree).toHaveBeenCalledWith('root', ['Ảnh'], 'merge');
    expect(api.findConflicts).toHaveBeenCalledWith('id:Ảnh', ['a.jpg', 'b.jpg']);
    expect(versions).toEqual(
      expect.arrayContaining([
        { name: 'báo cáo.pdf', target: 'existing:báo cáo.pdf', parentId: 'root' },
        { name: 'a.jpg', target: 'existing:a.jpg', parentId: 'id:Ảnh' },
      ]),
    );
    expect(added).toEqual([{ parentId: 'id:Ảnh', names: ['b.jpg'] }]);
  });

  it('thay thế: không có quyền sửa file đã có ⇒ giữ cả hai thay vì tạo phiên bản', async () => {
    const { api, added, versions } = fakeApi();
    await executeUpload(
      'root',
      fromFileList([fileAt('x.txt')]),
      [conflict('x.txt', 'file', 'viewer')],
      'replace',
      api,
    );
    expect(versions).toEqual([]);
    expect(added).toEqual([{ parentId: 'root', names: ['x.txt'] }]);
  });

  it('trùng tên ⇒ chờ người dùng chọn; huỷ thì không tải gì', async () => {
    const { api, added } = fakeApi();
    vi.mocked(api.findConflicts).mockResolvedValueOnce([conflict('x.txt', 'file')]);
    const planner = new UploadPlanner(api);

    await planner.upload('root', fromFileList([fileAt('x.txt')]));
    expect(planner.getSnapshot().pending?.conflicts).toHaveLength(1);

    await planner.resolve(null);
    expect(planner.getSnapshot().pending).toBeNull();
    expect(added).toEqual([]);
  });

  it('lỗi khi tạo thư mục được báo cho người dùng', async () => {
    const { api } = fakeApi();
    vi.mocked(api.createTree).mockRejectedValueOnce(new Error('boom'));
    const planner = new UploadPlanner(api);

    await planner.upload('root', fromFileList([fileAt('A/x.txt')]));

    expect(planner.getSnapshot().error).toBeInstanceOf(Error);
  });
});
