import { ApiError } from './http';

const messages: Record<string, string> = {
  'IDENTITY.INVALID_CREDENTIALS': 'Email hoặc mật khẩu không đúng.',
  'IDENTITY.EMAIL_TAKEN': 'Email này đã được đăng ký.',
  'IDENTITY.PASSWORD_MISMATCH': 'Mật khẩu hiện tại không đúng.',
  'IDENTITY.INVALID_REFRESH_TOKEN': 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
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
