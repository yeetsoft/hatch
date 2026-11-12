/* Pure functions for the attachment list beside a description or a comment -
   no DOM, no React. The hook that calls these lives in HA-1816. */
import type { Attachment } from '../types';

export { attachmentUrl } from './descriptionImage';

/** What is known about an id scanned out of markdown: the attachment itself,
    `null` when a fetch found it gone, or no entry at all when nothing has
    asked yet. */
export type Known = ReadonlyMap<string, Attachment | null>;

const ATTACHMENT_ID_PATTERN =
  /\/api\/hatch\/(?:images|attachments)\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/gi;

/** Every attachment id linked anywhere in `markdown`, lower-cased and in
    order of first appearance - a regex scan over the rendered path, not a
    markdown parse, so it catches an image, a link, a bare path and a full
    URL alike. */
export function linkedAttachmentIds(markdown: string): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const match of markdown.matchAll(ATTACHMENT_ID_PATTERN)) {
    const id = match[1].toLowerCase();
    if (!seen.has(id)) {
      seen.add(id);
      result.push(id);
    }
  }
  return result;
}

const UNITS = ['KB', 'MB', 'GB'] as const;

/** A byte count as a person reads it: whole bytes under 1 KB, otherwise the
    largest unit the value still clears, one decimal under 10 of that unit
    and none above. */
export function formatSize(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B';
  if (bytes < 1024) return `${Math.round(bytes)} B`;

  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < UNITS.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }

  const rounded = value < 10 ? Math.round(value * 10) / 10 : Math.round(value);
  const formatted = rounded % 1 === 0 ? String(rounded) : rounded.toFixed(1);
  return `${formatted} ${UNITS[unitIndex]}`;
}

/** The ids among `ids` that are neither already in `known` (present as
    `null` counts as known) nor already being fetched, in the order passed
    in. */
export function missingIds(ids: string[], known: Known, inFlight: ReadonlySet<string>): string[] {
  return ids.filter((id) => !known.has(id) && !inFlight.has(id));
}

/** `known` with every id in `asked` set to its match in `found`, or `null`
    when `found` has none - a new map, `known` itself is never mutated. */
export function settle(known: Known, asked: string[], found: Attachment[]): Known {
  const byId = new Map(found.map((a) => [a.id.toLowerCase(), a]));
  const result = new Map(known);
  for (const id of asked) {
    result.set(id, byId.get(id) ?? null);
  }
  return result;
}

/** `markdown` with every reference to `id` - image, link or bare path - struck,
    and the blank-line bookkeeping that removal leaves cleaned up: a line left
    blank by the removal is dropped rather than kept empty, a run of 3+
    newlines collapses to one blank line, and a leading or trailing run of
    newlines is stripped. Known gap: an alt text with a literal, un-escaped
    `]` can truncate the bracket match early - `attachmentMarkdown` never
    writes one. A second gap: `.trim()` (needed so trailing prose on the same
    line loses its single leading space) also drops a line's own structural
    indentation (a list item, a blockquote) when the reference opens that
    line - again never produced by `attachmentMarkdown` itself. */
export function removeAttachmentLinks(markdown: string, id: string): string {
  const path = `/api/(?:images|attachments)/${id}`;
  const pattern = new RegExp(`!?\\[(?:\\\\.|[^\\]\\n])*\\]\\(${path}\\)|${path}`, 'gi');

  const lines = markdown.split('\n').flatMap((line) => {
    const replaced = line.replace(pattern, '');
    if (replaced === line) return [line];
    if (replaced.trim() === '') return [];
    return [replaced.replace(/ {2,}/g, ' ').trim()];
  });

  return lines
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .replace(/^\n+/, '')
    .replace(/\n+$/, '');
}

/** `items` sliced into runs of at most `size`. */
export function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = [];
  for (let i = 0; i < items.length; i += size) {
    result.push(items.slice(i, i + size));
  }
  return result;
}

export type RowState = 'file' | 'deleted' | 'loading' | 'unavailable';

export interface AttachmentRow {
  id: string;
  state: RowState;
  attachment?: Attachment;
}

/** One row per id, in the order of `ids`. `known` wins over `failed`: a file
    or a deletion already heard back from the server outranks a request that
    merely failed to complete. */
export function attachmentRows(ids: string[], known: Known, failed: ReadonlySet<string>): AttachmentRow[] {
  return ids.map((id) => {
    if (known.has(id)) {
      const attachment = known.get(id);
      return attachment ? { id, state: 'file', attachment } : { id, state: 'deleted' };
    }
    if (failed.has(id)) return { id, state: 'unavailable' };
    return { id, state: 'loading' };
  });
}
