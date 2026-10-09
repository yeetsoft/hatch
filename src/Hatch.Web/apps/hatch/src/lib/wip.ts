/* The WIP section, apart from the page that draws it - the way lib/runners.ts
   is apart from the Runners page. Two halves live here: the Statuses page's
   checkbox and limit field below, and the board's band and drop preview
   further down.

   Pure functions taking the section and the board's statuses rather than
   reading either off a hook, so a toggle or a limit edit is a computation the
   page can test without a server. */

import type { IssueCard, IssueType, Status, Wip, WipSection, WipSliceSetting } from '../types';

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

/** Why a column's Agent files box is disabled, or null when it may be
    ticked - the same two reasons wipBlocked gives, read against the
    question this flag asks instead: not "in progress" but "born here". */
export function agentFilesBlocked(status: Status): string | null {
  if (status.isDeferred) return 'Deferred columns are parked work, never where new work is born';
  if (status.isTerminal) return 'Done columns are shipped work, never where new work is born';
  return null;
}

/** Why a column's Code written here box is disabled, or null when it may be
    ticked - the same two reasons wipBlocked gives, read against the
    question this flag asks instead: not "in progress" but "where code is
    written". */
export function implementationBlocked(status: Status): string | null {
  if (status.isDeferred) return 'Deferred columns are parked work, never where code is written';
  if (status.isTerminal) return 'Done columns are shipped work, never where code is written';
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

/** What a slice's limit field shows: blank for no limit, the number otherwise. */
export function limitDraft(slice: WipSliceSetting): string {
  return slice.limit === null ? '' : String(slice.limit);
}

/** The field, trimmed, as the wire's `limit`/`epicLimit` - blank clears it. */
export function limitRequest(draft: string): string {
  return draft.trim();
}

/** What a null EfHatchIssue.WipLimit reads as - one story at once. Mirrors
    EfHatchIssue.DefaultEpicWipLimit, the one constant that says so. */
export const DEFAULT_EPIC_WIP_LIMIT = 1;

/** What an epic's "Stories at once" field shows: blank for unset, the number
    otherwise. Bare rather than WipSliceSetting-shaped, like `limitDraft`
    above: an epic's limit is not a slice of the board. */
export function wipLimitDraft(limit: number | null): string {
  return limit === null ? '' : String(limit);
}

/** The field, trimmed, as the wire's `limit` - blank clears it back to
    DEFAULT_EPIC_WIP_LIMIT. */
export function wipLimitRequest(draft: string): string {
  return draft.trim();
}

/** How a set of issue types reads in a sentence - `stories and bugs`, `epics`.
    Mirrors Hatch.Contracts.TypeWords.Plural. */
export function plural(types: IssueType[]): string {
  const words = types.map((t) => (t.endsWith('y') ? `${t.slice(0, -1)}ies` : `${t}s`));
  if (words.length === 0) return 'issues';
  if (words.length === 1) return words[0];
  if (words.length === 2) return `${words[0]} and ${words[1]}`;
  return `${words.slice(0, -1).join(', ')}, and ${words[words.length - 1]}`;
}

/** How tight a slice reads: room for two or more, room for one, none left,
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
 * between two WIP ones (never drawn) does not split anything. No runs at all
 * where there is no section, or where no slice has a limit: no limit, no
 * band.
 */
export function runs(columns: Status[], wip: Wip | null): WipRun[] {
  if (!wip || !wip.slices.some((s) => s.limit !== null)) return [];
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

const TIGHTNESS_ORDER: Tightness[] = ['room', 'tight', 'full', 'over'];

/** The tightest reading among the slices that have a limit, or null where
    none does. */
export function tightest(wip: Wip, loads: number[]): Tightness | null {
  let worst: Tightness | null = null;
  wip.slices.forEach((slice, i) => {
    if (slice.limit === null) return;
    const t = tightness(loads[i], slice.limit);
    if (worst === null || TIGHTNESS_ORDER.indexOf(t) > TIGHTNESS_ORDER.indexOf(worst)) worst = t;
  });
  return worst;
}

/**
 * The load each slice's band draws, one entry per slice: one more than the
 * slice holds while a card of its type sits outside the section, mid-drag -
 * the slice's own load otherwise. `card` is the one being dragged or just
 * dropped; null leaves every slice at rest.
 */
export function preview(wip: Wip, card: IssueCard | null): number[] {
  return wip.slices.map((slice) =>
    card && slice.types.includes(card.type) && !wip.statusIds.includes(card.statusId) ? slice.load + 1 : slice.load,
  );
}

/** The band's text: `WIP 3 of 5 · epics 1 of 2`, with the claimed-inbound
    count and the tightness word appended per slice where they apply. Every
    part after the first is prefixed with its own types, pluralised, so a
    section with only the epic slice limited reads `WIP epics 1 of 2`.
    `loads` may hold the preview rather than each slice's own load - the
    wording follows whichever it is given. */
export function meterText(wip: Wip, loads: number[]): string {
  const parts = wip.slices
    .map((slice, i) => ({ slice, load: loads[i], prefix: i > 0 ? `${plural(slice.types)} ` : '' }))
    .filter(({ slice }) => slice.limit !== null)
    .map(({ slice, load, prefix }) => {
      const limit = slice.limit!;
      const claimed = slice.claimedInbound > 0 ? ` (${slice.claimedInbound} on the way)` : '';
      const state = load > limit ? ' · over' : load === limit ? ' · full' : '';
      return `${prefix}${load} of ${limit}${claimed}${state}`;
    });

  return `WIP ${parts.join(' · ')}`;
}
