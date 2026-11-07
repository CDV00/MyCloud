import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import { Button, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { useAuth } from '../auth/authContext';

export default function HomePage() {
  const { user } = useAuth();

  return (
    <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
      <Typography variant="h5" component="h1">
        Chào mừng {user?.displayName} đến với MyCloud
      </Typography>
      <Typography color="text.secondary">
        Mục “Được đề xuất” và “Gần đây” sẽ có ở Phase 3, khi Search service theo dõi lượt mở file.
      </Typography>
      <Button component={RouterLink} to="/my-drive" variant="outlined" startIcon={<CloudQueueIcon />}>
        Mở Drive của tôi
      </Button>
    </Stack>
  );
}
