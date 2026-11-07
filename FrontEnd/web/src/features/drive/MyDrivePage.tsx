import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import FolderIcon from '@mui/icons-material/Folder';
import InsertDriveFileOutlinedIcon from '@mui/icons-material/InsertDriveFileOutlined';
import {
  Alert,
  Box,
  Button,
  List,
  ListItemButton,
  ListItem,
  ListItemIcon,
  ListItemText,
  Skeleton,
  Stack,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink, useParams } from 'react-router';
import { getFolder, listFiles, listFolders } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { formatSize } from '../../lib/format';
import { driveKeys } from './queries';

/**
 * "Drive của tôi" bản tối thiểu của Phase 1 — chứng minh luồng đăng nhập → gọi API có token → dữ liệu
 * chỉ của chính mình. Lưới/danh sách, breadcrumb, sắp xếp, upload... làm ở Phase 2.
 */
export default function MyDrivePage() {
  const { folderId = null } = useParams();

  const folder = useQuery({
    queryKey: driveKeys.folder(folderId ?? ''),
    queryFn: () => getFolder(folderId!),
    enabled: folderId !== null,
  });
  const folders = useQuery({ queryKey: driveKeys.folders(folderId), queryFn: () => listFolders(folderId) });
  const files = useQuery({
    queryKey: driveKeys.files(folderId ?? ''),
    queryFn: () => listFiles(folderId!),
    enabled: folderId !== null,
  });

  const parentLink = folder.data?.parentId ? `/my-drive/folders/${folder.data.parentId}` : '/my-drive';
  const error = folders.error ?? folder.error ?? files.error;
  const loading = folders.isPending || (folderId !== null && (folder.isPending || files.isPending));
  const empty = !loading && !error && !folders.data?.length && !files.data?.length;

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        {folderId && (
          <Button component={RouterLink} to={parentLink} startIcon={<ArrowBackIcon />} size="small">
            Quay lại
          </Button>
        )}
        <Typography variant="h5" component="h1">
          {folderId ? (folder.data?.name ?? '…') : 'Drive của tôi'}
        </Typography>
      </Stack>

      {error && <Alert severity="error">{toUserMessage(error)}</Alert>}

      {loading && (
        <Box>
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} height={48} />
          ))}
        </Box>
      )}

      {empty && (
        <Box sx={{ textAlign: 'center', py: 8, color: 'text.secondary' }}>
          <Typography variant="h6">Chưa có gì ở đây</Typography>
          <Typography>Bấm “Mới” → “Thư mục mới” để bắt đầu.</Typography>
        </Box>
      )}

      {!loading && !error && !empty && (
        <List aria-label="Nội dung thư mục">
          {folders.data?.map((f) => (
            <ListItemButton
              key={f.id}
              component={RouterLink}
              to={`/my-drive/folders/${f.id}`}
              sx={{ borderRadius: 2 }}
            >
              <ListItemIcon>
                <FolderIcon sx={{ color: 'text.secondary' }} />
              </ListItemIcon>
              <ListItemText primary={f.name} secondary={f.description || null} />
            </ListItemButton>
          ))}
          {files.data?.map((f) => (
            <ListItem key={f.id} sx={{ borderRadius: 2 }}>
              <ListItemIcon>
                <InsertDriveFileOutlinedIcon sx={{ color: 'text.secondary' }} />
              </ListItemIcon>
              <ListItemText primary={f.name} secondary={formatSize(f.sizeBytes)} />
            </ListItem>
          ))}
        </List>
      )}
    </Stack>
  );
}
