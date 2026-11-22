/* How long a card has sat in its column, and the words for it.

   The server decides whether a card is aged: it holds the column's normal (the
   90th percentile of finished stays over the last 30 days) and the events that
   say when the card arrived. This module only reads that verdict and draws it,
   so the elapsed time is worked out against the server's own clock - a browser
   whose clock is wrong cannot disagree with `aged`. */

import type { ColumnNormal, IssueCard, Status } from '../types';

export interface CardAge {
  /** The time in the column, as a short span: `14h 20m`. */
  words: string;
  /** The tooltip: the column, the time, and what normal is there. */
  title: string;
}

/**
 * A span of seconds in the one or two units a glance needs. Each unit is
 * floored, and a trailing zero unit is dropped: `9h`, not `9h 0m`.
 */
export function formatSpan(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  if (total < 60) return '<1m';
  const minutes = Math.floor(total / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    const m = minutes % 60;
    return m === 0 ? `${hours}h` : `${hours}h ${m}m`;
  }
  const days = Math.floor(hours / 24);
  const h = hours % 24;
  return h === 0 ? `${days}d` : `${days}d ${h}h`;
}

/**
 * What a card says about its age, or null for a card that says nothing.
 *
 * Null unless the server marked it `aged`, it knows when the card entered the
 * column, and the column has a normal. Any one missing and there is nothing
 * honest to put on the card.
 */
export function cardAge(
  card: Pick<IssueCard, 'aged' | 'enteredColumnAt'>,
  status: Pick<Status, 'id' | 'name'>,
  normals: ColumnNormal[] | undefined,
  at?: string,
): CardAge | null {
  if (!card.aged || !card.enteredColumnAt) return null;
  const normal = normals?.find((n) => n.statusId === status.id)?.seconds;
  if (normal == null) return null;

  const entered = Date.parse(card.enteredColumnAt);
  const now = at ? Date.parse(at) : Date.now();
  if (!Number.isFinite(entered) || !Number.isFinite(now)) return null;

  const words = formatSpan((now - entered) / 1000);
  return {
    words,
    title: `In ${status.name} for ${words}. Normal here is under ${formatSpan(normal)} (p90, last 30 days).`,
  };
}
