import { Alert, LinearProgress } from '@mui/material';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { getDownloadUrl } from '../api/drive';
import { toUserMessage } from '../api/errors';
import { downloadItem, folderPath } from '../features/drive/download';
import { useItem } from '../features/drive/queries';
import { editorStore } from '../features/office/editorStore';
import { FilePreview } from '../features/preview/FilePreview';

/**
 * Mở thẳng một tệp: <c>/files/:id</c> (link từ thông báo trao đổi, <c>?panel=history</c> mở sẵn "Lịch sử & trao đổi").
 * Đóng ⇒ về trang trước, hoặc thư mục chứa tệp nếu mở từ link.
 */
export default function FilePage() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const item = useItem(id);

  if (item.error) return <Alert severity="error">{toUserMessage(item.error)}</Alert>;
  if (!item.data) return <LinearProgress />;
  const file = item.data;

  const close = () => {
    if (window.history.length > 1) navigate(-1);
    else
      navigate(
        file.parentId && file.role === 'owner' ? folderPath({ id: file.parentId, isRoot: false }) : '/shared',
      );
  };

  return (
    <FilePreview
      items={[file]}
      index={0}
      onIndexChange={(index) => index === null && close()}
      getUrl={async (f) => (await getDownloadUrl(f.id, { inline: true })).url}
      onDownload={(f) => void downloadItem(f.id)}
      onEdit={(f) => editorStore.open(f)}
      history
      historyOpen={params.get('panel') === 'history'}
    />
  );
}
