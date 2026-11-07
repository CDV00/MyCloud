import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Snackbar,
} from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router';
import {
  copyItem,
  getDownloadUrl,
  purgeItem,
  restoreItem,
  setStar,
  trashItem,
  type DriveItem,
} from '../../api/drive';
import { toUserMessage } from '../../api/errors';
import { archiveStore } from '../archive/archiveStore';
import { editorStore } from '../office/editorStore';
import { FilePreview } from '../preview/FilePreview';
import { uploadStore } from '../upload/uploadStore';
import { DetailsPanel, type DetailsTab } from './DetailsPanel';
import { downloadItem, folderPath } from './download';
import { MoveDialog } from './MoveDialog';
import { refreshDrive } from './queries';
import { ShareDialog } from '../sharing/ShareDialog';
import { RenameDialog } from './RenameDialog';

interface Notice {
  text: string;
  severity: 'success' | 'error' | 'info';
  undo?: () => void;
}

export interface ItemActions {
  /** Thư mục ⇒ mở; file ⇒ xem trước (chuyển qua lại giữa các file của danh sách đang xem). */
  open(item: DriveItem): void;
  download(item: DriveItem, versionId?: string): void;
  /** Mở trình sửa (văn bản, CSV, Excel, Word) — lưu thành phiên bản mới. */
  edit(item: Pick<DriveItem, 'id' | 'name'>): void;
  /** Một file ⇒ tải thẳng; thư mục / nhiều mục ⇒ tệp zip tạo trong nền. */
  downloadMany(items: DriveItem[]): void;
  rename(item: DriveItem): void;
  move(items: DriveItem[]): void;
  copy(item: DriveItem): void;
  star(items: DriveItem[], starred: boolean): void;
  trash(items: DriveItem[]): void;
  restore(items: DriveItem[]): void;
  /** Hỏi xác nhận rồi xoá vĩnh viễn. */
  purge(items: DriveItem[]): void;
  details(item: DriveItem, tab?: DetailsTab): void;
  share(item: Pick<DriveItem, 'id' | 'name'>): void;
  uploadVersion(item: Pick<DriveItem, 'id' | 'name' | 'parentId'>): void;
  showLocation(item: Pick<DriveItem, 'parentId'>): void;
  /** Hộp thoại, panel, thông báo — đặt một lần trong trang. */
  elements: ReactNode;
}

const plural = (items: DriveItem[]) => (items.length === 1 ? `“${items[0]!.name}”` : `${items.length} mục`);

/** Mọi thao tác trên item dùng chung cho các trang (Drive của tôi, Có gắn dấu sao, Gần đây, Tìm kiếm, Thùng rác). */
export function useItemActions(
  options: {
    onChanged?: () => void;
    /** Danh sách đang hiển thị — để xem trước file kế tiếp. */ items?: DriveItem[];
  } = {},
): ItemActions {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const versionInput = useRef<HTMLInputElement>(null);
  const [versionTarget, setVersionTarget] = useState<Pick<DriveItem, 'id' | 'name' | 'parentId'> | null>(
    null,
  );
  const [renaming, setRenaming] = useState<DriveItem | null>(null);
  const [moving, setMoving] = useState<DriveItem[]>([]);
  const [details, setDetails] = useState<{ id: string; tab: DetailsTab } | null>(null);
  const [sharing, setSharing] = useState<Pick<DriveItem, 'id' | 'name'> | null>(null);
  const [purging, setPurging] = useState<DriveItem[]>([]);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [preview, setPreview] = useState<{ items: DriveItem[]; index: number } | null>(null);

  const changed = () => {
    refreshDrive(queryClient);
    options.onChanged?.();
  };

  async function run(work: () => Promise<unknown>, success?: Notice) {
    try {
      await work();
      if (success) setNotice(success);
    } catch (e) {
      setNotice({ text: toUserMessage(e), severity: 'error' });
    } finally {
      changed();
    }
  }

  const base: Omit<ItemActions, 'elements'> = {
    open(item) {
      if (item.kind === 'folder') {
        navigate(folderPath(item));
        return;
      }

      const files = (options.items ?? []).filter((i) => i.kind === 'file');
      const index = files.findIndex((i) => i.id === item.id);
      setPreview(index >= 0 ? { items: files, index } : { items: [item], index: 0 });
    },
    download: (item, versionId) => void run(() => downloadItem(item.id, versionId)),
    edit: (item) => editorStore.open(item),
    downloadMany(items) {
      if (items.length === 1 && items[0]!.kind === 'file') base.download(items[0]!);
      else void archiveStore.request(items);
    },
    rename: setRenaming,
    move: setMoving,
    copy: (item) =>
      void run(() => copyItem(item.id, { name: `Bản sao của ${item.name}` }), {
        text: `Đã tạo bản sao của “${item.name}”`,
        severity: 'success',
      }),
    star: (items, starred) =>
      void run(() => Promise.all(items.map((i) => setStar(i.id, starred))), {
        text: starred ? `Đã gắn dấu sao ${plural(items)}` : `Đã bỏ dấu sao ${plural(items)}`,
        severity: 'success',
      }),
    trash: (items) =>
      void run(() => Promise.all(items.map((i) => trashItem(i.id))), {
        text: `Đã chuyển ${plural(items)} vào thùng rác`,
        severity: 'info',
        undo: () => base.restore(items),
      }),
    restore: (items) =>
      void run(() => Promise.all(items.map((i) => restoreItem(i.id))), {
        text: `Đã khôi phục ${plural(items)}`,
        severity: 'success',
      }),
    purge: setPurging,
    details: (item, tab = 'details') => setDetails({ id: item.id, tab }),
    share: setSharing,
    uploadVersion(item) {
      setVersionTarget(item);
      versionInput.current?.click();
    },
    showLocation: (item) => {
      if (item.parentId) navigate(`/my-drive/folders/${item.parentId}`);
    },
  };

  // Panel chi tiết gọi lại các thao tác (không cần phần tử giao diện).
  const panelActions: ItemActions = { ...base, elements: null };

  const elements = (
    <>
      <FilePreview
        items={preview?.items ?? []}
        index={preview?.index ?? null}
        onIndexChange={(index) => setPreview((p) => (index === null || !p ? null : { ...p, index }))}
        getUrl={async (item) => (await getDownloadUrl(item.id, { inline: true })).url}
        onDownload={(item) => base.download(item)}
        onEdit={(item) => {
          setPreview(null);
          editorStore.open(item);
        }}
        history
      />
      <RenameDialog item={renaming} onClose={() => setRenaming(null)} />
      <MoveDialog items={moving} onClose={() => setMoving([])} />
      <ShareDialog item={sharing} onClose={() => setSharing(null)} />
      <DetailsPanel
        itemId={details?.id ?? null}
        tab={details?.tab ?? 'details'}
        onTabChange={(tab) => setDetails((d) => (d ? { ...d, tab } : d))}
        onClose={() => setDetails(null)}
        actions={panelActions}
      />
      <Dialog open={purging.length > 0} onClose={() => setPurging([])} maxWidth="xs">
        <DialogTitle>Xoá vĩnh viễn?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {plural(purging)} sẽ bị xoá vĩnh viễn cùng mọi phiên bản và nội dung bên trong. Bạn không thể hoàn
            tác thao tác này.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPurging([])}>Huỷ</Button>
          <Button
            color="error"
            variant="contained"
            onClick={() => {
              const items = purging;
              setPurging([]);
              void run(() => Promise.all(items.map((i) => purgeItem(i.id))), {
                text: `Đã xoá vĩnh viễn ${plural(items)}`,
                severity: 'success',
              });
            }}
          >
            Xoá vĩnh viễn
          </Button>
        </DialogActions>
      </Dialog>
      <input
        ref={versionInput}
        type="file"
        hidden
        aria-hidden
        onChange={(e) => {
          const file = e.target.files?.[0];
          if (file && versionTarget) uploadStore.addVersion(file, versionTarget);
          e.target.value = '';
        }}
      />
      <Snackbar
        open={notice !== null}
        autoHideDuration={notice?.undo ? 8000 : 5000}
        onClose={(_, reason) => {
          if (reason !== 'clickaway') setNotice(null);
        }}
      >
        <Alert
          severity={notice?.severity ?? 'info'}
          onClose={() => setNotice(null)}
          action={
            notice?.undo ? (
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  notice.undo?.();
                  setNotice(null);
                }}
              >
                Huỷ
              </Button>
            ) : undefined
          }
        >
          {notice?.text}
        </Alert>
      </Snackbar>
    </>
  );

  return { ...base, elements };
}
