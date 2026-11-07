import { ApiError } from './http';

const messages: Record<string, string> = {
  'IDENTITY.INVALID_CREDENTIALS': 'Email hoặc mật khẩu không đúng.',
  'IDENTITY.EMAIL_TAKEN': 'Email này đã được đăng ký.',
  'IDENTITY.PASSWORD_MISMATCH': 'Mật khẩu hiện tại không đúng.',
  'IDENTITY.INVALID_REFRESH_TOKEN': 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
  'DRIVE.NAME_CONFLICT': 'Thư mục này đã có mục cùng tên.',
  'DRIVE.INVALID_NAME':
    'Tên không hợp lệ (không được chứa dấu / hoặc dấu gạch chéo ngược, tối đa 255 ký tự).',
  'DRIVE.INVALID_MOVE': 'Không thể di chuyển thư mục vào chính nó hoặc thư mục con của nó.',
  'DRIVE.REVISION_MISMATCH': 'Mục này vừa được thay đổi ở nơi khác. Tải lại trang rồi thử lại.',
  'DRIVE.ITEM_NOT_FOUND': 'Không tìm thấy mục này (có thể đã bị xoá hoặc bạn không có quyền).',
  'DRIVE.ROOT_IMMUTABLE': 'Không thể đổi tên hoặc di chuyển “Drive của tôi”.',
  'STORAGE.QUOTA_EXCEEDED': 'Không đủ dung lượng trống cho tệp này.',
  'STORAGE.FILE_TOO_LARGE': 'Tệp vượt quá kích thước tối đa cho phép (5 GB).',
  'STORAGE.SIZE_MISMATCH': 'Tệp bị thay đổi trong lúc tải lên. Vui lòng thử lại.',
};

/** Thông báo lỗi tiếng Việt cho người dùng từ lỗi API (ProblemDetails). */
export function toUserMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const code = error.problem.code;
    if (code && messages[code]) {
      return messages[code];
    }
    if (error.status === 429) {
      return 'Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.';
    }
    if (error.status === 400 && error.problem.errors?.length) {
      return error.problem.errors.map((e) => e.message).join(' ');
    }
    if (error.status >= 500) {
      return 'Hệ thống đang gặp sự cố. Vui lòng thử lại sau.';
    }
    return error.problem.detail ?? error.message;
  }
  return 'Không kết nối được tới máy chủ.';
}
