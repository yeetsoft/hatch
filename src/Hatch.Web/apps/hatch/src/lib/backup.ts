import type { BackupStatus } from '../types';

/**
 * The sentences the Backups card says, pulled out of the component so they
 * can be read - and tested - without a browser, the way `hatchSettings.ts`
 * and `attention.ts` already are.
 */

/** Which of the three faces the card draws. Named for what is true rather
    than for how it looks, so a later redesign of the card is not a rename. */
export type BackupState = 'off' | 'none' | 'kept';

/** `'off'` with no folder set, `'none'` once one is set but nothing has been
    kept yet, `'kept'` once there is at least one copy on disk. */
export function backupState(s: BackupStatus): BackupState {
  if (!s.configured) return 'off';
  return s.copies.length === 0 ? 'none' : 'kept';
}

/** The whole of what the card says when there is no folder at all. The Docs
    link that follows it is the component's own, not a sentence here. */
export const OFF_WORDS = 'No backup folder is set, so nothing is being backed up.';

/** `Last successful backup: <local date and time>`, or the sentence for a
    folder that has never produced one - which is the normal state the moment
    a folder is first set, not a failure. */
export function lastSuccessWords(s: Pick<BackupStatus, 'lastSuccessfulAt'>, locale?: string, timeZone?: string): string {
  if (s.lastSuccessfulAt === null) return 'There has never been a successful backup.';
  return `Last successful backup: ${localDateTime(s.lastSuccessfulAt, locale, timeZone)}`;
}

/** `3 copies kept, 12.4 MB in all` - the count and the total size of what is
    on disk, singular at one copy. */
export function keptWords(s: BackupStatus): string {
  const n = s.copies.length;
  const totalBytes = s.copies.reduce((sum, c) => sum + c.bytes, 0);
  return `${n} ${n === 1 ? 'copy' : 'copies'} kept, ${formatBytes(totalBytes)} in all`;
}

/** `Folder: <path>`, or the folder's own problem sentence in its place when it
    cannot be read - that sentence already names the folder and why, so it
    replaces the plain line rather than appending to it. */
export function folderWords(s: BackupStatus): string {
  if (!s.folderReadable && s.folderProblem !== null) return s.folderProblem;
  return `Folder: ${s.path ?? ''}`;
}

/** `The last attempt failed: <lastError> (<local date and time>)`, or `null`
    when the last attempt did not fail. Drawn beside `lastSuccessWords`, which
    may name an older, successful attempt - the two are never the same line,
    so an error is never allowed to hide a success that still stands. */
export function errorWords(s: BackupStatus, locale?: string, timeZone?: string): string | null {
  if (s.lastOutcome !== 'failed') return null;
  const error = s.lastError ?? '';
  if (s.lastAttemptAt === null) return `The last attempt failed: ${error}`;
  return `The last attempt failed: ${error} (${localDateTime(s.lastAttemptAt, locale, timeZone)})`;
}

/** Whether the *Back up now* button is live: a folder to back up to, and no
    attempt already running. */
export const canRunNow = (s: BackupStatus, running: boolean): boolean => s.configured && !running;

/** `512 bytes`, `12.4 MB` - bytes scaled to the first unit that reads under a
    thousand of it, one decimal place from KB up. No code here builds a file
    name out of a byte count or a date; this only ever reads the `bytes` field
    already on a `BackupCopy`. */
function formatBytes(bytes: number): string {
  const units = ['KB', 'MB', 'GB'];
  if (bytes < 1024) return `${bytes} bytes`;

  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(1)} ${units[unit]}`;
}

/** The local wall time for an ISO instant, in the reader's locale and zone
    unless a test pins one. */
function localDateTime(iso: string, locale?: string, timeZone?: string): string {
  return new Date(iso).toLocaleString(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone });
}
