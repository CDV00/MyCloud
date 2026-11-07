export const driveKeys = {
  all: ['drive'] as const,
  folders: (parentId: string | null) => ['drive', 'folders', parentId ?? 'root'] as const,
  folder: (folderId: string) => ['drive', 'folder', folderId] as const,
  files: (folderId: string) => ['drive', 'files', folderId] as const,
};
