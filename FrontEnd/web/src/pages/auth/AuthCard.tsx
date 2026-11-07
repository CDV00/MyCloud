import CloudIcon from '@mui/icons-material/Cloud';
import { Box, Paper, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';

/** Khung chung cho trang đăng nhập / đăng ký. */
export function AuthCard({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: ReactNode;
}) {
  return (
    <Box
      sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', bgcolor: 'background.default', p: 2 }}
    >
      <Paper variant="outlined" sx={{ width: '100%', maxWidth: 420, p: { xs: 3, sm: 5 }, borderRadius: 3 }}>
        <Stack spacing={1} sx={{ mb: 3, alignItems: 'center', textAlign: 'center' }}>
          <CloudIcon color="primary" sx={{ fontSize: 48 }} />
          <Typography variant="h5" component="h1">
            {title}
          </Typography>
          <Typography color="text.secondary">{subtitle}</Typography>
        </Stack>
        {children}
      </Paper>
    </Box>
  );
}
