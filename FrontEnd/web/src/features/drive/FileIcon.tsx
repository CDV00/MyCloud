import ArticleOutlinedIcon from '@mui/icons-material/ArticleOutlined';
import AudioFileOutlinedIcon from '@mui/icons-material/AudioFileOutlined';
import FolderIcon from '@mui/icons-material/Folder';
import FolderZipOutlinedIcon from '@mui/icons-material/FolderZipOutlined';
import ImageOutlinedIcon from '@mui/icons-material/ImageOutlined';
import InsertDriveFileOutlinedIcon from '@mui/icons-material/InsertDriveFileOutlined';
import PictureAsPdfOutlinedIcon from '@mui/icons-material/PictureAsPdfOutlined';
import VideoFileOutlinedIcon from '@mui/icons-material/VideoFileOutlined';
import type { SvgIconProps } from '@mui/material';
import type { DriveItem } from '../../api/drive';

/** Biểu tượng theo loại nội dung (thumbnail thật ở Phase 5). */
export function FileIcon({ item, ...props }: { item: Pick<DriveItem, 'kind' | 'mimeType'> } & SvgIconProps) {
  if (item.kind === 'folder') return <FolderIcon sx={{ color: '#5f6368' }} {...props} />;
  const mime = item.mimeType ?? '';
  if (mime.startsWith('image/')) return <ImageOutlinedIcon sx={{ color: '#d93025' }} {...props} />;
  if (mime.startsWith('video/')) return <VideoFileOutlinedIcon sx={{ color: '#d93025' }} {...props} />;
  if (mime.startsWith('audio/')) return <AudioFileOutlinedIcon sx={{ color: '#7b1fa2' }} {...props} />;
  if (mime === 'application/pdf') return <PictureAsPdfOutlinedIcon sx={{ color: '#d93025' }} {...props} />;
  if (/zip|compressed|tar|rar|7z/.test(mime))
    return <FolderZipOutlinedIcon sx={{ color: '#5f6368' }} {...props} />;
  if (mime.startsWith('text/') || /word|document|sheet|presentation/.test(mime)) {
    return <ArticleOutlinedIcon sx={{ color: '#1a73e8' }} {...props} />;
  }
  return <InsertDriveFileOutlinedIcon sx={{ color: '#5f6368' }} {...props} />;
}
