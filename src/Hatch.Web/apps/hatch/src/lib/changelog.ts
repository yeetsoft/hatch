/* The changelog page's pure helpers, in words - no React, no fetch, mirroring
   overnight.ts's own split between a page component and what it draws. */

import type { ChangelogEntryDto } from '../types';

/** The page's empty state - one wording, like overnight.ts's own. */
export const changelogEmptyWords = (): string => 'Nothing has shipped yet.';

/** `Tuesday, October 13, 2026`. `date` is the server's `yyyy-MM-dd` UTC day,
    formatted in UTC so the runtime's zone never moves it. The optional
    locale exists so tests can pin one, as `ticks` in timeline.ts does. */
export function dayHeading(date: string, locale?: string): string {
  return new Date(`${date}T00:00:00Z`).toLocaleDateString(locale, {
    weekday: 'long', month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC',
  });
}

/** A fix's one-liner: its release note's first line, or its title when the note is null/blank. */
export function fixLine(entry: Pick<ChangelogEntryDto, 'title' | 'releaseNote'>): string {
  const note = entry.releaseNote?.trim();
  if (!note) return entry.title;
  return note.split('\n')[0].trim();
}
