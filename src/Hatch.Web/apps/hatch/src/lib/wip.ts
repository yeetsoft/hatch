/* The WIP section, apart from the page that draws it - the way lib/runners.ts
   is apart from the Runners page. Two halves live here: the Statuses page's
   checkbox and limit field below, and the board's band and drop preview
   further down.

   Pure functions taking the section and the board's statuses rather than
   reading either off a hook, so a toggle or a limit edit is a computation the
   page can test without a server. */

import type { IssueCard, Status, Wip, WipSection } from '../types';

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

/** How tight the section is: room for two or more, room for one, none left,
    or already past it. */
export type Tightness = 'room' | 'tight' | 'full' | 'over';

/** One band's worth of columns: a run of adjacent WIP columns among the
    board's drawn ones, `start` its index among them. */
export interface WipRun {
  start: number;
  statusIds: number[];
}

/**
 * Each contiguous run of WIP columns in `columns` - the board's own drawn
 * order, deferred columns already gone - so a section split by a column
 * outside it comes back as two runs sharing one meter, and a deferred column
 * between two WIP ones (never drawn) does not split anything. `null` gives no
 * runs at all: no limit, no band.
 */
export function runs(columns: Status[], wip: Wip | null): WipRun[] {
  if (!wip) return [];
  const counted = new Set(wip.statusIds);

  const found: WipRun[] = [];
  let current: WipRun | null = null;
  columns.forEach((status, at) => {
    if (!counted.has(status.id)) {
      current = null;
      return;
    }
    if (!current) {
      current = { start: at, statusIds: [] };
      found.push(current);
    }
    current.statusIds.push(status.id);
  });

  return found;
}

/** How tight a load against a limit reads: two or more left is room, one left
    is tight, none left is full, and past it is over. */
export function tightness(load: number, limit: number): Tightness {
  const left = limit - load;
  if (left >= 2) return 'room';
  if (left === 1) return 'tight';
  if (left === 0) return 'full';
  return 'over';
}

/**
 * The load the band draws: one more than the section holds while a counted
 * card sits outside it, mid-drag - the section's own load otherwise. `card`
 * is the one being dragged or just dropped; null leaves the band at rest.
 */
export function preview(wip: Wip, card: IssueCard | null): number {
  if (card && wip.types.includes(card.type) && !wip.statusIds.includes(card.statusId)) {
    return wip.load + 1;
  }
  return wip.load;
}

/** The band's text: `WIP 3 of 5`, with the claimed-inbound count and the
    tightness word appended where they apply. `load` may be the preview
    rather than `wip.load` itself - the wording follows whichever it is
    given. */
export function meterText(wip: Wip, load: number): string {
  const claimed = wip.claimedInbound > 0 ? ` (${wip.claimedInbound} on the way)` : '';
  const state = load > wip.limit ? ' · over' : load === wip.limit ? ' · full' : '';
  return `WIP ${load} of ${wip.limit}${claimed}${state}`;
}
