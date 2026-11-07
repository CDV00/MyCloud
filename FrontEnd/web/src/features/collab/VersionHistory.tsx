import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import EditNoteIcon from '@mui/icons-material/EditNote';
import ForumOutlinedIcon from '@mui/icons-material/ForumOutlined';
import RestoreIcon from '@mui/icons-material/Restore';
import UploadFileOutlinedIcon from '@mui/icons-material/UploadFileOutlined';
import VisibilityOutlinedIcon from '@mui/icons-material/VisibilityOutlined';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useState } from 'react';
import { listComments, updateVersionNote } from '../../api/collab';
import { canEdit, deleteVersion, restoreVersion, type DriveItem, type FileVersion } from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { useAuth } from '../../auth/authContext';
import { formatDateTime } from '../drive/describe';
import { driveKeys, refreshDrive, useVersions } from '../drive/queries';
import { useUsers } from '../people/useUsers';
import { formatSize } from '../../lib/format';
import { commentKeys } from './useItemRoom';

const VersionDiffDialog = lazy(() => import('./VersionDiffDialog'));

function describeSource(v: FileVersion): string {
  switch (v.source) {
    case 'edit':
      return 'đã chỉnh sửa';
    case 'restore':
      return v.restoredFromVersionNo ? `đã khôi phục phiên bản ${v.restoredFromVersionNo}` : 'đã khôi phục';
    case 'copy':
      return 'đã tạo bản sao';
    default:
      return 'đã tải lên';
  }
}

/**
 * Các phiên bản của tệp (ADR-017): ai, khi nào, bằng cách nào, ghi chú (lý do); xem, so sánh với bản trước, góp ý,
 * khôi phục (kèm lý do — tạo phiên bản mới nên không mất các bản sau), tải về, xoá.
 */
export function VersionHistory({
  item,
  onView,
  onDiscuss,
  onUploadVersion,
  onDownload,
}: {
  item: DriveItem;
  onView?(version: FileVersion): void;
  /** Mở trao đổi về phiên bản này. */
  onDiscuss(version: FileVersion): void;
  onUploadVersion?(): void;
  onDownload(version: FileVersion): void;
}) {
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const versions = useVersions(item.id);
  const comments = useQuery({ queryKey: commentKeys.item(item.id), queryFn: () => listComments(item.id) });
  const users = useUsers((versions.data ?? []).map((v) => v.createdBy));
  const [diff, setDiff] = useState<{ older: FileVersion; newer: FileVersion } | null>(null);
  const [restoring, setRestoring] = useState<FileVersion | null>(null);
  const [reason, setReason] = useState('');
  const [noteFor, setNoteFor] = useState<{ version: FileVersion; text: string } | null>(null);
  const editable = canEdit(item) && item.trashedAt === null;

  const refresh = () => {
    refreshDrive(queryClient);
    void queryClient.invalidateQueries({ queryKey: driveKeys.versions(item.id) });
  };
  const action = useMutation({ mutationFn: (work: () => Promise<unknown>) => work(), onSettled: refresh });

  const threadsOf = (v: FileVersion) =>
    (comments.data ?? []).filter((c) => c.parentId === null && c.versionId === v.id && !c.deleted).length;
  const nameOf = (id: string) => (id === user?.id ? 'Bạn' : (users.get(id)?.displayName ?? '…'));
  const list = versions.data ?? [];

  return (
    <Stack spacing={1.5} sx={{ p: 2 }}>
      {onUploadVersion && editable && (
        <Button
          variant="outlined"
          startIcon={<UploadFileOutlinedIcon />}
          onClick={onUploadVersion}
          sx={{ alignSelf: 'flex-start' }}
        >
          Tải lên phiên bản mới
        </Button>
      )}
      {action.error && <Alert severity="error">{toUserMessage(action.error)}</Alert>}
      {versions.isPending && <CircularProgress size={24} />}
      {list.map((v, index) => {
        const previous = list[index + 1];
        const threads = threadsOf(v);
        const canNote = v.createdBy === user?.id || item.ownerId === user?.id;
        return (
          <Box
            key={v.id}
            sx={{ borderLeft: 3, borderColor: v.isCurrent ? 'primary.main' : 'divider', pl: 1.5 }}
          >
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Typography variant="subtitle2">Phiên bản {v.versionNo}</Typography>
              {v.isCurrent && <Chip label="Hiện tại" size="small" color="primary" variant="outlined" />}
              {threads > 0 && (
                <Chip
                  size="small"
                  icon={<ForumOutlinedIcon />}
                  label={`${threads} góp ý`}
                  onClick={() => onDiscuss(v)}
                />
              )}
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {nameOf(v.createdBy)} {describeSource(v)} · {formatDateTime(v.createdAt)} ·{' '}
              {formatSize(v.sizeBytes)}
            </Typography>

            {noteFor?.version.id === v.id ? (
              <Stack direction="row" spacing={1} sx={{ mt: 1, alignItems: 'flex-start' }}>
                <TextField
                  autoFocus
                  fullWidth
                  multiline
                  size="small"
                  maxRows={4}
                  placeholder="Ghi chú thay đổi (lý do sửa)…"
                  value={noteFor.text}
                  onChange={(e) => setNoteFor({ version: v, text: e.target.value })}
                  slotProps={{
                    htmlInput: { maxLength: 1000, 'aria-label': `Ghi chú phiên bản ${v.versionNo}` },
                  }}
                />
                <Button
                  size="small"
                  variant="contained"
                  onClick={() => {
                    const text = noteFor.text;
                    setNoteFor(null);
                    action.mutate(() => updateVersionNote(item.id, v.id, text));
                  }}
                >
                  Lưu
                </Button>
                <Button size="small" onClick={() => setNoteFor(null)}>
                  Huỷ
                </Button>
              </Stack>
            ) : (
              v.note && (
                <Typography variant="body2" sx={{ mt: 0.5, fontStyle: 'italic', whiteSpace: 'pre-wrap' }}>
                  “{v.note}”
                </Typography>
              )
            )}

            <Stack direction="row" sx={{ mt: 0.5, ml: -0.75 }}>
              {onView && (
                <Tooltip title="Xem phiên bản này">
                  <IconButton
                    size="small"
                    onClick={() => onView(v)}
                    aria-label={`Xem phiên bản ${v.versionNo}`}
                  >
                    <VisibilityOutlinedIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              )}
              {previous && (
                <Tooltip title={`So sánh với phiên bản ${previous.versionNo}`}>
                  <IconButton
                    size="small"
                    onClick={() => setDiff({ older: previous, newer: v })}
                    aria-label={`So sánh phiên bản ${v.versionNo} với phiên bản ${previous.versionNo}`}
                  >
                    <CompareArrowsIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              )}
              <Tooltip title="Góp ý phiên bản này">
                <IconButton
                  size="small"
                  onClick={() => onDiscuss(v)}
                  aria-label={`Góp ý phiên bản ${v.versionNo}`}
                >
                  <ForumOutlinedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
              {canNote && item.trashedAt === null && (
                <Tooltip title={v.note ? 'Sửa ghi chú' : 'Thêm ghi chú thay đổi'}>
                  <IconButton
                    size="small"
                    onClick={() => setNoteFor({ version: v, text: v.note ?? '' })}
                    aria-label={`Ghi chú phiên bản ${v.versionNo}`}
                  >
                    <EditNoteIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              )}
              <Tooltip title="Tải xuống phiên bản này">
                <IconButton
                  size="small"
                  onClick={() => onDownload(v)}
                  aria-label={`Tải xuống phiên bản ${v.versionNo}`}
                >
                  <DownloadIcon fontSize="small" />
                </IconButton>
              </Tooltip>
              {!v.isCurrent && editable && (
                <>
                  <Tooltip title="Khôi phục phiên bản này">
                    <IconButton
                      size="small"
                      disabled={action.isPending}
                      onClick={() => {
                        setReason('');
                        setRestoring(v);
                      }}
                      aria-label={`Khôi phục phiên bản ${v.versionNo}`}
                    >
                      <RestoreIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Xoá phiên bản này">
                    <IconButton
                      size="small"
                      disabled={action.isPending}
                      onClick={() => action.mutate(() => deleteVersion(item.id, v.id))}
                      aria-label={`Xoá phiên bản ${v.versionNo}`}
                    >
                      <DeleteOutlineIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </>
              )}
            </Stack>
          </Box>
        );
      })}
      {list.length > 0 && (
        <Typography variant="caption" color="text.secondary">
          Mỗi lần tải lên, chỉnh sửa hoặc khôi phục tạo một phiên bản mới; giữ tối đa 100 phiên bản gần nhất.
        </Typography>
      )}

      <Dialog open={restoring !== null} onClose={() => setRestoring(null)} maxWidth="xs" fullWidth>
        <DialogTitle>Khôi phục phiên bản {restoring?.versionNo}?</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            Nội dung của phiên bản {restoring?.versionNo} sẽ thành phiên bản mới nhất. Các phiên bản hiện có
            vẫn được giữ.
          </DialogContentText>
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={2}
            label="Lý do khôi phục (không bắt buộc)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 1000 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRestoring(null)}>Huỷ</Button>
          <Button
            variant="contained"
            onClick={() => {
              const v = restoring!;
              setRestoring(null);
              action.mutate(() => restoreVersion(item.id, v.id, reason));
            }}
          >
            Khôi phục
          </Button>
        </DialogActions>
      </Dialog>

      {diff && (
        <Suspense fallback={null}>
          <VersionDiffDialog
            item={item}
            older={diff.older}
            newer={diff.newer}
            onClose={() => setDiff(null)}
          />
        </Suspense>
      )}
    </Stack>
  );
}
