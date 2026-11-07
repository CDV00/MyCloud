import type { GrantRole } from '../../api/drive';

export const roleLabels: Record<GrantRole, string> = {
  viewer: 'Người xem',
  commenter: 'Người nhận xét',
  editor: 'Người chỉnh sửa',
};
