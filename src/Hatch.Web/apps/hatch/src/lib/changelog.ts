/* The changelog page's pure helpers, in words - no React, no fetch, mirroring
   overnight.ts's own split between a page component and what it draws. */

import type { ChangelogDto, ChangelogEntryDto, ChangelogMonthDto } from '../types';

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

/** The window held with the next one below it. The days already held stay in
    place and ahead; a day the next window repeats is listed once. `hasOlder`
    and `oldest` are the next window's - it is the one that reaches furthest
    back. `months` is the same on every answer, so the newest is taken. */
export function appendWindow(held: ChangelogDto | null, next: ChangelogDto): ChangelogDto {
  if (held === null) return next;
  const have = new Set(held.days.map((day) => day.date));
  return {
    days: [...held.days, ...next.days.filter((day) => !have.has(day.date))],
    hasOlder: next.hasOlder,
    oldest: next.oldest ?? held.oldest,
    months: next.months,
  };
}

/** The `before` that asks for the window below what is held: the oldest day.
    `before` is exclusive and that whole day is already held, so the window
    below has no overlap and no gap. `null` when there is nothing older. */
export function nextBefore(c: ChangelogDto): string | null {
  return c.hasOlder ? c.oldest : null;
}

/** Whether **Older** is offered at all. */
export function canLoadOlder(c: ChangelogDto): boolean {
  return nextBefore(c) !== null;
}

/** The `before` a month press means: the first day of the following month, as
    a bare date (UTC midnight). `before` is exclusive, so the whole month is
    below it. `month` is the server's `yyyy-MM`. */
export function monthBefore(month: string): string {
  const [year, number] = month.split('-').map(Number);
  const next = number === 12 ? [year + 1, 1] : [year, number + 1];
  return `${String(next[0]).padStart(4, '0')}-${String(next[1]).padStart(2, '0')}-01`;
}

/** `October 2026 · 12`: the month, and how many issues closed in it. Formatted
    in UTC, like `dayHeading`, with the same optional locale for tests. */
export function monthLabel(m: ChangelogMonthDto, locale?: string): string {
  const name = new Date(`${m.month}-01T00:00:00Z`).toLocaleDateString(locale, {
    month: 'long', year: 'numeric', timeZone: 'UTC',
  });
  return `${name} · ${m.count}`;
}

/** The address bar's `?before=`, or `null` when it is absent or blank. */
export function parseBefore(params: URLSearchParams): string | null {
  const before = params.get('before')?.trim();
  return before ? before : null;
}
