import CloseIcon from '@mui/icons-material/Close';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import {
  Avatar,
  AvatarGroup,
  Badge,
  Box,
  IconButton,
  Stack,
  Tab,
  Tabs,
  Tooltip,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { listComments } from '../../api/collab';
import { getDownloadUrl, type DriveItem, type FileVersion } from '../../api/drive';
import { downloadItem } from '../drive/download';
import { useVersions } from '../drive/queries';
import { FilePreview } from '../preview/FilePreview';
import { Discussion, type ComposerTarget } from './Discussion';
import { commentKeys, useItemRoom, type Present } from './useItemRoom';
import { VersionHistory } from './VersionHistory';

export type HistoryTab = 'discussion' | 'versions';

/** Người đang mở tệp — "đang sửa" có dấu bút. */
export function PresenceAvatars({ people }: { people: Present[] }) {
  if (people.length === 0) return null;
  return (
    <AvatarGroup max={4} sx={{ '& .MuiAvatar-root': { width: 26, height: 26, fontSize: 12 } }}>
      {people.map((p) => (
        <Tooltip key={p.userId} title={`${p.name} ${p.mode === 'editing' ? 'đang sửa' : 'đang xem'}`}>
          <Badge
            overlap="circular"
            anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            badgeContent={
              p.mode === 'editing' ? (
                <EditOutlinedIcon
                  sx={{ fontSize: 11, bgcolor: 'warning.main', color: 'white', borderRadius: '50%' }}
                />
              ) : null
            }
          >
            <Avatar alt={p.name}>{p.name.split(/\s+/).pop()?.[0]?.toUpperCase()}</Avatar>
          </Badge>
        </Tooltip>
      ))}
    </AvatarGroup>
  );
}

/**
 * Lịch sử & trao đổi của tệp (ADR-017) — dùng trong bảng chi tiết, khi xem trước và khi đang sửa: các phiên bản (ai,
 * khi nào, lý do, so sánh, khôi phục) và trao đổi realtime giữa những người cùng làm trên tệp.
 */
export function FileHistoryPanel({
  item,
  mode = 'viewing',
  initialTab = 'discussion',
  onClose,
  onUploadVersion,
}: {
  item: DriveItem;
  mode?: 'viewing' | 'editing';
  initialTab?: HistoryTab;
  onClose?(): void;
  onUploadVersion?(): void;
}) {
  const people = useItemRoom(item.id, mode);
  const versions = useVersions(item.id);
  const comments = useQuery({ queryKey: commentKeys.item(item.id), queryFn: () => listComments(item.id) });
  const [tab, setTab] = useState<HistoryTab>(initialTab);
  const [target, setTarget] = useState<ComposerTarget | null>(null);
  const [filter, setFilter] = useState<{ id: string; no: number } | null>(null);
  const [viewing, setViewing] = useState<FileVersion | null>(null);
  const canComment = ['owner', 'editor', 'commenter'].includes(item.role) && item.trashedAt === null;
  const open = (comments.data ?? []).filter((c) => c.parentId === null && !c.resolvedAt && !c.deleted).length;

  const discuss = (v: FileVersion) => {
    setFilter({ id: v.id, no: v.versionNo });
    setTarget({ versionId: v.id, versionNo: v.versionNo });
    setTab('discussion');
  };

  return (
    <Stack sx={{ height: '100%', minHeight: 0, bgcolor: 'background.paper', color: 'text.primary' }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 2, pt: 1.5 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 600, flexGrow: 1 }}>
          Lịch sử & trao đổi
        </Typography>
        <PresenceAvatars people={people} />
        {onClose && (
          <IconButton size="small" onClick={onClose} aria-label="Đóng lịch sử & trao đổi">
            <CloseIcon fontSize="small" />
          </IconButton>
        )}
      </Stack>
      <Tabs
        value={tab}
        onChange={(_, v: HistoryTab) => setTab(v)}
        sx={{ px: 1, borderBottom: 1, borderColor: 'divider' }}
      >
        <Tab value="discussion" label={open > 0 ? `Trao đổi (${open})` : 'Trao đổi'} />
        <Tab value="versions" label={versions.data ? `Phiên bản (${versions.data.length})` : 'Phiên bản'} />
      </Tabs>
      <Box sx={{ flexGrow: 1, minHeight: 0, overflow: tab === 'versions' ? 'auto' : 'hidden' }}>
        {tab === 'discussion' ? (
          <Discussion
            item={item}
            canComment={canComment}
            target={target}
            onTargetChange={setTarget}
            versionFilter={filter}
            onVersionFilter={(f) => {
              setFilter(f);
              if (!f) setTarget(null);
            }}
          />
        ) : (
          <VersionHistory
            item={item}
            onView={setViewing}
            onDiscuss={discuss}
            onUploadVersion={onUploadVersion}
            onDownload={(v) => void downloadItem(item.id, v.id)}
          />
        )}
      </Box>

      <FilePreview
        items={
          viewing
            ? [
                {
                  ...item,
                  name: `${item.name} — phiên bản ${viewing.versionNo}`,
                  mimeType: viewing.mimeType,
                  sizeBytes: viewing.sizeBytes,
                  versionNo: viewing.versionNo,
                  hasThumbnail: false,
                },
              ]
            : []
        }
        index={viewing ? 0 : null}
        onIndexChange={(index) => index === null && setViewing(null)}
        getUrl={async () => (await getDownloadUrl(item.id, { inline: true, versionId: viewing!.id })).url}
        onDownload={() => viewing && void downloadItem(item.id, viewing.id)}
      />
    </Stack>
  );
}
