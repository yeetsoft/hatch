/* Which stories an epic carries, and how to say how far along they are.

   It is a module rather than a helper inside BoardPage for the reason
   lineage.ts and columns.ts both give in their own headers: getting it
   slightly wrong has no visible symptom - a story simply shows in the wrong
   place, or a count reads a little off - and there is no DOM in the web test
   run, so the decisions live in a lib and get tested there.

   A project that turns `storiesOnEpics` on draws its stories on the epic card
   rather than as cards of their own. A story is carried when its project has
   the flag on, it is a story, and its parent is an epic standing in a column
   the board draws. Everything else keeps its own card: a bug, task or
   reflection under an epic, a story under a reflection, a sub-epic (which
   carries its own stories the same way), and a story whose epic is missing,
   shelved, or itself past the horizon. The epic is looked up among the cards
   being drawn only - never among the ones past the horizon - since an epic
   that is not drawn has no card to carry anything on.

   A story past the horizon is still counted on its epic, but never joins the
   carried set: it was never in the drawn cards, so there is no column entry
   to take out. Nothing here touches React or the DOM, and nothing mutates
   its inputs.

   It also remembers which epics a browser is shown with collapsed - a
   browser's own choice, with no bearing on what gets carried. */

import type { IssueCard, Project, Status } from '../types';
import { boardColumns } from './columns';

const byRankThenKey = (a: IssueCard, b: IssueCard): number => a.rank - b.rank || a.key.localeCompare(b.key);

/**
 * @param cards Every card the board is drawing, epics and stories alike.
 * @param pastHorizon Cards dropped past the horizon: counted on their epic, never removed from a column.
 * @param statuses Every status, deferred ones included - the drawn columns are derived here.
 * @param projectsByKey Projects by key; one missing from the map carries nothing.
 * @returns `carried`, the keys in `cards` to take out of their columns, and
 *   `storiesByEpic`, each epic's stories (both sides, rank then key) - an epic
 *   with none has no entry.
 */
export function carried(
  cards: IssueCard[],
  pastHorizon: IssueCard[],
  statuses: Status[],
  projectsByKey: Map<string, Project>,
): { carried: Set<string>; storiesByEpic: Map<string, IssueCard[]> } {
  const drawn = new Set(boardColumns(statuses).map((s) => s.id));
  const byKey = new Map(cards.map((c) => [c.key, c]));

  const carries = (card: IssueCard): boolean => {
    if (!projectsByKey.get(card.projectKey)?.storiesOnEpics) return false;
    if (card.type !== 'story' || card.parentKey === null) return false;
    const epic = byKey.get(card.parentKey);
    return epic !== undefined && epic.type === 'epic' && drawn.has(epic.statusId);
  };

  const carriedKeys = new Set<string>();
  const storiesByEpic = new Map<string, IssueCard[]>();
  const add = (card: IssueCard) => {
    const list = storiesByEpic.get(card.parentKey as string);
    if (list) list.push(card);
    else storiesByEpic.set(card.parentKey as string, [card]);
  };

  for (const card of cards) {
    if (!carries(card)) continue;
    carriedKeys.add(card.key);
    add(card);
  }
  for (const card of pastHorizon) {
    if (carries(card)) add(card);
  }

  for (const [epic, list] of storiesByEpic) storiesByEpic.set(epic, [...list].sort(byRankThenKey));
  return { carried: carriedKeys, storiesByEpic };
}

/**
 * How many of an epic's stories there are and how many have shipped. Done is
 * the terminal column only: a story in a protected column, or a deferred one,
 * is not done, and a status id nobody knows counts as not done.
 */
export function storyCount(stories: IssueCard[], statuses: Status[]): { total: number; done: number } {
  return {
    total: stories.length,
    done: stories.filter((s) => statuses.find((st) => st.id === s.statusId)?.isTerminal === true).length,
  };
}

/** The count as it is said on the card - `countWords(3, 1)` reads "1 of 3 done". */
export function countWords(total: number, done: number): string {
  return `${done} of ${total} done`;
}

/** Where the collapsed epics are persisted - namespaced next to
    `hatch.board.types` (lib/filter.ts). */
export const COLLAPSED_EPICS_STORAGE_KEY = 'hatch.board.collapsedEpics';

/** The keys of the epics this browser has collapsed, or none - try/catch for
    the same reason `readBoardTypes` does one: localStorage throws in a
    partitioned or locked-down context, and the board still has to render.
    Only strings are kept. Nothing is pruned when an epic leaves the board, so
    an epic hidden by a filter keeps its state. Always a fresh Set. */
export function readCollapsedEpics(): Set<string> {
  try {
    const stored: unknown = JSON.parse(window.localStorage.getItem(COLLAPSED_EPICS_STORAGE_KEY) ?? 'null');
    if (Array.isArray(stored)) {
      return new Set(stored.filter((key): key is string => typeof key === 'string'));
    }
  } catch {
    /* See above. */
  }
  return new Set();
}

/** Persists the keys. A store that cannot be written is not worth surfacing:
    the choice holds until the next reload, as the board's types do. */
export function writeCollapsedEpics(keys: Iterable<string>): void {
  try {
    window.localStorage.setItem(COLLAPSED_EPICS_STORAGE_KEY, JSON.stringify([...keys]));
  } catch {
    /* See above. */
  }
}

/** A new Set with `key` collapsed, or expanded again if it was collapsed.
    Expanded is the default and is never stored, so expanding deletes the key.
    The input is not mutated. */
export function toggleCollapsed(collapsed: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(collapsed);
  if (!next.delete(key)) next.add(key);
  return next;
}
