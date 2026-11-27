import type { AmbitionChangeLine } from '../types';

/* The words of the Ambitions page's "Since you last looked" section. */

export const CHANGES_HEADING = 'Since you last looked';

export const FIRST_VISIT_NOTE = 'This is your first visit, so this covers the last seven days.';

export const NO_CHANGES = 'Nothing has changed since you last looked.';

/** The label before a line's words, by kind. */
export const KIND_WORDS: Record<AmbitionChangeLine['kind'], string> = {
  holds: 'Now holds',
  carved: 'Carved',
  decided: 'Decided without you',
  asked: 'Asked you',
};

const count = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;

/**
 * How long ago the instant the section runs from was: `under a minute ago`,
 * `4 minutes ago`, `2 hours ago`, `3 days ago`. Counted as `waitedWords`
 * counts, which says "just asked" and so cannot be reused. Null for an
 * instant that does not parse: the heading then stands alone.
 */
export function sinceWords(since: string, now: Date): string | null {
  const at = Date.parse(since);
  if (Number.isNaN(at)) return null;

  const minutes = Math.floor(Math.max(0, now.getTime() - at) / 60_000);
  if (minutes < 1) return 'under a minute ago';
  if (minutes < 60) return `${count(minutes, 'minute')} ago`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${count(hours, 'hour')} ago`;

  return `${count(Math.floor(hours / 24), 'day')} ago`;
}

/** Which capabilities a carved epic serves: `serves #1, #2`. */
export function servesWords(serves: number[] | null): string {
  if (!serves || serves.length === 0) return 'serves no capability yet';
  return `serves ${serves.map((position) => `#${position}`).join(', ')}`;
}

/** The last line, where the read held more than it answered. */
export const omittedWords = (omitted: number): string => `and ${omitted} more`;
