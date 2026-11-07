/** Dung lượng dễ đọc theo định dạng số tiếng Việt: 1536 ⇒ "1,5 KB". */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toLocaleString('vi-VN', { maximumFractionDigits: 1 })} ${units[unit]}`;
}

/** Chữ cái đầu của họ và tên cuối cho avatar: "Nguyễn Văn An" ⇒ "NA". */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  const letters = parts.length > 1 ? [parts[0]![0], parts[parts.length - 1]![0]] : [parts[0]?.[0]];
  return letters.filter(Boolean).join('').toUpperCase() || '?';
}
