import { Button, Stack, Typography } from '@mui/material';
import { isRouteErrorResponse, Link as RouterLink, useRouteError } from 'react-router';

export default function ErrorPage() {
  const error = useRouteError();
  const notFound = isRouteErrorResponse(error) && error.status === 404;

  return (
    <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center', py: 12, px: 2 }}>
      <Typography variant="h4" component="h1">
        {notFound ? 'Không tìm thấy trang' : 'Đã có lỗi xảy ra'}
      </Typography>
      <Typography color="text.secondary">
        {notFound ? 'Đường dẫn không tồn tại hoặc đã bị xoá.' : 'Vui lòng tải lại trang hoặc thử lại sau.'}
      </Typography>
      <Button component={RouterLink} to="/" variant="contained">
        Về trang chủ
      </Button>
    </Stack>
  );
}
