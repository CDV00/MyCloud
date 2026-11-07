import ConstructionIcon from '@mui/icons-material/Construction';
import { Stack, Typography } from '@mui/material';

/** Trang giữ chỗ cho mục menu chưa làm — ghi rõ phase sẽ làm theo docs/07. */
export default function ComingSoonPage({ title, phase }: { title: string; phase: number }) {
  return (
    <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center', py: 8, color: 'text.secondary' }}>
      <ConstructionIcon sx={{ fontSize: 56 }} />
      <Typography variant="h5" component="h1" color="text.primary">
        {title}
      </Typography>
      <Typography>Tính năng này sẽ có ở Phase {phase}.</Typography>
    </Stack>
  );
}
