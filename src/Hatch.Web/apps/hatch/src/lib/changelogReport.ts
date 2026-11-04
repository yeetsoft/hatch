/* The changelog report's own arithmetic: what the URL says about bucket size,
   level and loose work, what a bucket is called, and where the buckets leave a
   gap. Nothing here touches React or the DOM, so what can be wrong lives in a
   function with a test beside it.

   The project filter is lib/timeline.ts's own (`PROJECT`, `resolveProjects`,
   `withProjects`); the page imports it from there and nothing here copies it. */

const MINUTE_MS = 60_000;
const DAY_MS = 86_400_000;

export const BUCKET = 'bucket';
export const LEVEL = 'level';
export const LOOSE = 'loose';

export type ReportBucketSize = 'hour' | 'day' | 'week' | 'month';

const BUCKET_SIZES: ReportBucketSize[] = ['hour', 'day', 'week', 'month'];

/** The bucket size the URL names, or `day`: trimmed and lower-cased first, and
    anything that is not one of the four is a typo, not an error. */
export function resolveBucket(params: URLSearchParams): ReportBucketSize {
  const raw = params.get(BUCKET)?.trim().toLowerCase();

  return BUCKET_SIZES.find((size) => size === raw) ?? 'day';
}

/** A copy of `params` with the bucket size set, or removed at `day` - the
    default is not written to the URL. Every other param is kept; the input is
    never mutated. */
export function withBucket(params: URLSearchParams, bucket: ReportBucketSize): URLSearchParams {
  const next = new URLSearchParams(params);
  if (bucket === 'day') next.delete(BUCKET);
  else next.set(BUCKET, bucket);

  return next;
}

/** `epic` only when the URL says exactly that (trimmed), else `story`. */
export function resolveLevel(params: URLSearchParams): 'epic' | 'story' {
  return params.get(LEVEL)?.trim() === 'epic' ? 'epic' : 'story';
}

/** A copy of `params` with the level set, or removed at `story`. */
export function withLevel(params: URLSearchParams, level: 'epic' | 'story'): URLSearchParams {
  const next = new URLSearchParams(params);
  if (level === 'story') next.delete(LEVEL);
  else next.set(LEVEL, level);

  return next;
}

/** Whether loose work is asked for: only the exact text `true`. */
export const resolveLoose = (params: URLSearchParams): boolean => params.get(LOOSE) === 'true';

/** A copy of `params` with loose set to `true`, or removed when false. */
export function withLoose(params: URLSearchParams, loose: boolean): URLSearchParams {
  const next = new URLSearchParams(params);
  if (loose) next.set(LOOSE, 'true');
  else next.delete(LOOSE);

  return next;
}

/**
 * What a bucket is called, in the reader's own clock. `startIso` is the
 * bucket's start as the server cut it; `offsetMinutes` (minutes east of UTC)
 * shifts it onto the reader's wall clock, which is then formatted as UTC so
 * the runtime's zone never moves it, as `ticks` in timeline.ts does.
 *
 * - `hour` - `4 Nov, 14:00`.
 * - `day` - `Wednesday, November 4, 2026`, as the changelog's day heading.
 * - `week` - `2 Nov – 8 Nov 2026`: the Monday the server cut the week at, to
 *   the Sunday six days on.
 * - `month` - `November 2026`.
 *
 * The optional locale exists so the tests can pin one; the page passes none
 * and gets the reader's.
 */
export function bucketLabel(
  startIso: string,
  bucket: ReportBucketSize,
  offsetMinutes: number,
  locale?: string,
): string {
  const shifted = Date.parse(startIso) + offsetMinutes * MINUTE_MS;
  const date = (at: number, options: Intl.DateTimeFormatOptions) =>
    new Date(at).toLocaleDateString(locale, { ...options, timeZone: 'UTC' });
  const time = (at: number, options: Intl.DateTimeFormatOptions) =>
    new Date(at).toLocaleTimeString(locale, { ...options, timeZone: 'UTC' });

  switch (bucket) {
    case 'hour':
      return `${date(shifted, { day: 'numeric', month: 'short' })}, ${time(shifted, { hour: '2-digit', minute: '2-digit' })}`;
    case 'day':
      return date(shifted, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    case 'week':
      return `${date(shifted, { day: 'numeric', month: 'short' })} – ${date(shifted + 6 * DAY_MS, { day: 'numeric', month: 'short', year: 'numeric' })}`;
    case 'month':
      return date(shifted, { month: 'long', year: 'numeric' });
  }
}

/** Whether a stretch no bucket covers lies between two neighbours. Buckets run
    newest first, and `end` is exclusive, so buckets that touch have no gap. */
export const hasQuietGap = (newer: { start: string }, older: { end: string }): boolean =>
  Date.parse(older.end) < Date.parse(newer.start);

/** The gap `hasQuietGap` found, in words: `Quiet from 2 Nov 2026 to 9 Nov 2026`. */
export function quietGapWords(
  newer: { start: string },
  older: { end: string },
  offsetMinutes: number,
  locale?: string,
): string {
  const day = (iso: string) =>
    new Date(Date.parse(iso) + offsetMinutes * MINUTE_MS).toLocaleDateString(locale, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });

  return `Quiet from ${day(older.end)} to ${day(newer.start)}`;
}

/** The page's empty state - one wording, like changelog.ts's own. */
export const changelogReportEmptyWords = (): string => 'Nothing landed in this window.';
