export function downloadText(
  content: string,
  filename: string,
  mimeType = "text/plain;charset=utf-8",
): void {
  const blob = new Blob([content], { type: mimeType });
  triggerDownload(blob, filename);
}

export function downloadBytes(
  bytes: Uint8Array,
  filename: string,
  mimeType = "application/octet-stream",
): void {
  const blob = new Blob([new Uint8Array(bytes)], { type: mimeType });
  triggerDownload(blob, filename);
}

function triggerDownload(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
