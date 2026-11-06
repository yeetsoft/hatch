/* Pure functions for an image pasted into a description or a comment - no DOM,
   no canvas. This app's vitest runs in plain Node, so the half that touches
   createImageBitmap and a canvas lives in MarkdownEditor.tsx. */
import { message } from './errors';
import { LOGO_ACCEPTED_TYPES } from './projectLogo';

/** The largest body the server stores. Mirrors PersonPhoto.MaxBytes
    (src/Hatch.Api/Common/PersonPhoto.cs), which is the only place it is
    enforced - this copy decides only when to shrink before sending. */
export const IMAGE_MAX_BYTES = 2 * 1024 * 1024;

/** How many times a pasted image is re-encoded smaller before it is sent as it
    is and the server's refusal is what the person reads. */
export const SHRINK_ATTEMPTS = 4;

export const imageUrl = (id: string) => `/api/hatch/images/${id}`;

export const imageMarkdown = (id: string) => `![image](${imageUrl(id)})`;

/** The first file on the clipboard Hatch can store, or null when the paste is
    something else - in which case the paste is left alone. */
export function pastedImage<T extends { type: string }>(files: readonly T[]): T | null {
  return files.find((f) => LOGO_ACCEPTED_TYPES.includes(f.type)) ?? null;
}

/** The size to draw a too-large image at, or null when it already fits. Both
    sides scale by the square root of the byte ratio - bytes follow area - aimed
    at 90% of the cap so the re-encode lands under it rather than on it. */
export function shrinkTarget(
  width: number,
  height: number,
  bytes: number,
  maxBytes = IMAGE_MAX_BYTES,
): { width: number; height: number } | null {
  if (bytes <= maxBytes) return null;
  const scale = Math.sqrt((0.9 * maxBytes) / bytes);
  return { width: Math.max(1, Math.floor(width * scale)), height: Math.max(1, Math.floor(height * scale)) };
}

/** What a canvas re-encodes a source type as. `toBlob` cannot write GIF, so
    GIF - and PNG, which is lossless - come out as PNG. */
export function encodeType(sourceType: string): 'image/jpeg' | 'image/webp' | 'image/png' {
  if (sourceType === 'image/jpeg') return 'image/jpeg';
  if (sourceType === 'image/webp') return 'image/webp';
  return 'image/png';
}

/** The textarea splice: `text` replaces [start, end) and the caret lands after it. */
export function insertText(value: string, start: number, end: number, text: string): { value: string; caret: number } {
  return { value: value.slice(0, start) + text + value.slice(end), caret: start + text.length };
}

export const attachmentUrl = (id: string) => `/api/hatch/attachments/${id}`;

/** Whether a drag carries at least one file, as opposed to dragged text or a
    link - both of which also populate `DataTransfer.types` but never with
    `'Files'`. */
export function dragHasFiles(types: readonly string[] | null | undefined): boolean {
  return (types ?? []).includes('Files');
}

/** The markdown for an uploaded attachment - image syntax for a type the
    editor would also preview, link syntax for everything else. */
export function attachmentMarkdown(a: { id: string; fileName: string; contentType: string }): string {
  const isImage = LOGO_ACCEPTED_TYPES.includes(a.contentType);
  const escaped = a.fileName.replace(/\\/g, '\\\\').replace(/\[/g, '\\[').replace(/\]/g, '\\]');
  const name = escaped === '' ? (isImage ? 'image' : 'attachment') : escaped;
  return isImage ? `![${name}](${imageUrl(a.id)})` : `[${name}](${attachmentUrl(a.id)})`;
}

/** `value` with `block` appended after exactly one blank line - none when
    `value` already ends in one, and `block` alone when `value` is empty or
    whitespace-only. */
export function appendBlock(value: string, block: string): string {
  if (value.trim() === '') return block;
  if (/\n\n$/.test(value)) return value + block;
  if (/\n$/.test(value)) return value + '\n' + block;
  return value + '\n\n' + block;
}

/** Uploads `files` one after another, in order, turning each into its
    markdown and joining the results with a newline. Stops at the first
    rejection - later files are never passed to `upload` at all - and throws
    an error naming the failing file when more than one was dropped. */
export async function markdownForDrop<F extends { name: string }>(
  files: readonly F[],
  upload: (file: F) => Promise<{ id: string; contentType: string }>,
): Promise<string> {
  const blocks: string[] = [];
  for (const file of files) {
    let uploaded: { id: string; contentType: string };
    try {
      uploaded = await upload(file);
    } catch (err) {
      throw new Error(files.length === 1 ? message(err) : `${file.name}: ${message(err)}`);
    }
    blocks.push(attachmentMarkdown({ id: uploaded.id, fileName: file.name, contentType: uploaded.contentType }));
  }
  return blocks.join('\n');
}
