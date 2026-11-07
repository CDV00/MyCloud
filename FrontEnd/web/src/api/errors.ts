import { ApiError } from './http';

const messages: Record<string, string> = {
  'IDENTITY.INVALID_CREDENTIALS': 'Email hoặc mật khẩu không đúng.',
  'IDENTITY.EMAIL_TAKEN': 'Email này đã được đăng ký.',
  'IDENTITY.PASSWORD_MISMATCH': 'Mật khẩu hiện tại không đúng.',
  'IDENTITY.INVALID_REFRESH_TOKEN': 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.',
  'DRIVE.NAME_CONFLICT': 'Thư mục này đã có mục cùng tên.',
  'DRIVE.INVALID_NAME':
    'Tên không hợp lệ (không được chứa dấu / hoặc dấu gạch chéo ngược, tối đa 255 ký tự).',
  'DRIVE.INVALID_MOVE':
    'Không thể di chuyển tới đó (thư mục vào chính nó / thư mục con, hoặc sang Drive của người khác — hãy tạo bản sao).',
  'DRIVE.REVISION_MISMATCH': 'Mục này vừa được thay đổi ở nơi khác. Tải lại trang rồi thử lại.',
  'DRIVE.ITEM_NOT_FOUND': 'Không tìm thấy mục này (có thể đã bị xoá hoặc bạn không có quyền).',
  'DRIVE.ROOT_IMMUTABLE': 'Không thể thực hiện thao tác này với “Drive của tôi”.',
  'DRIVE.ITEM_TRASHED': 'Mục này đang ở thùng rác. Hãy khôi phục trước khi thay đổi.',
  'DRIVE.NOT_IN_TRASH': 'Mục này không còn ở thùng rác.',
  'DRIVE.RESTORE_CONTAINING_FOLDER': 'Mục này bị xoá cùng thư mục chứa nó. Hãy khôi phục thư mục đó.',
  'DRIVE.VERSION_NOT_FOUND': 'Không tìm thấy phiên bản này (có thể đã bị xoá).',
  'DRIVE.CANNOT_DELETE_CURRENT_VERSION': 'Không thể xoá phiên bản hiện tại.',
  'DRIVE.ACCESS_DENIED': 'Bạn không có quyền thực hiện thao tác này với mục này.',
  'DRIVE.USER_NOT_FOUND': 'Không có tài khoản MyCloud nào dùng email này.',
  'DRIVE.ALREADY_OWNER': 'Người này là chủ sở hữu, đã có toàn quyền.',
  'DRIVE.LINK_NOT_FOUND': 'Đường liên kết không tồn tại, đã hết hạn hoặc đã bị tắt.',
  'DRIVE.LINK_PASSWORD_REQUIRED': 'Đường liên kết này cần mật khẩu.',
  'DRIVE.COPY_TOO_LARGE': 'Thư mục quá lớn để sao chép một lần (tối đa 500 mục).',
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
