/** Tải nội dung file (presigned URL cùng origin). */
export async function fetchBytes(url: string): Promise<ArrayBuffer> {
  const response = await fetch(url);
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  return response.arrayBuffer();
}

export const decodeText = (bytes: ArrayBuffer) => new TextDecoder('utf-8').decode(bytes);

/** Dấu thứ tự byte UTF-8 (U+FEFF) — viết bằng mã số để không lẫn với khoảng trắng vô hình trong mã nguồn. */
export const BOM = String.fromCharCode(0xfeff);
