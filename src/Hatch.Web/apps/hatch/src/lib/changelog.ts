/* The changelog report's pure helper, in words - no React, no fetch. */

import type { ChangelogEntryDto } from '../types';

/** A fix's one-liner: its release note's first line, or its title when the note is null/blank. */
export function fixLine(entry: Pick<ChangelogEntryDto, 'title' | 'releaseNote'>): string {
  const note = entry.releaseNote?.trim();
  if (!note) return entry.title;
  return note.split('\n')[0].trim();
}
