/* The WIP section, apart from the page that draws it - the way lib/runners.ts
   is apart from the Runners page.

   Pure functions taking the section and the board's statuses rather than
   reading either off a hook, so a toggle or a limit edit is a computation the
   page can test without a server. */

import type { Status, WipSection } from '../types';

/** Why a column's box is disabled, or null when it may be ticked.

    Deferred and terminal are refused by the server the same way - see
    WipController - and the title here is why, not a repeat of the server's
    sentence: the box is disabled before a request is ever sent, so nobody
    reads the refusal at all. */
export function wipBlocked(status: Status): string | null {
  if (status.isDeferred) return 'Deferred columns are parked work, never work in progress';
  if (status.isTerminal) return 'Done columns are shipped work, never work in progress';
  return null;
}

/** The next `statusIds` after ticking or unticking one column - in board
    order, which is the order `statuses` already holds. */
export function toggled(section: WipSection, statuses: Status[], id: number, on: boolean): number[] {
  const wanted = new Set(section.statusIds);
  if (on) wanted.add(id);
  else wanted.delete(id);
  return statuses.filter((s) => wanted.has(s.id)).map((s) => s.id);
}

/** What the limit field shows: blank for no limit, the number otherwise. */
export function limitDraft(section: WipSection): string {
  return section.limit === null ? '' : String(section.limit);
}

/** The field, trimmed, as the wire's `limit` - blank clears it. */
export function limitRequest(draft: string): string {
  return draft.trim();
}
