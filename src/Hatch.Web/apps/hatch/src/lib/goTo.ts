/* The console's decisions: what a query finds, in what order, what the status
   line says, and where the card is on the board.

   In a module with no React in it for the reason lib/issuePicker.ts is: the rule
   goes in `lib/` and is tested, and the layout and the keys are looked at in a
   browser. What is left in OmniBar and BoardPage is state and events.

   Keys come first, then titles. Somebody who types a number wants the issue with
   that number, and a title that happens to contain it must not take its place.

   The console follows the tab's scope: an issue outside it is not found by a
   prefix or by words from its title. The one way in is typing its whole key,
   which can only mean that issue, and the row says it is out of scope. */

import { compareIssueKeys, matchesText, splitKey } from './issuePicker';
import { cardsInScope } from './scope';
import { searchPath } from './search';
import type { IssueCard, Status } from '../types';

/**
 * How many rows the console draws. The board holds every issue in the house, and
 * `1` matches a tenth of them: hundreds of rows is a page nobody reads, and the
 * status line says `50 of 112 — keep typing` instead.
 */
export const GO_TO_LIMIT = 50;

/**
 * How long typing must pause before the console asks the server for the rows it
 * does not hold. The Search page waits 300; the console is for a key already in
 * mind, so it waits a little less.
 */
export const GO_TO_DEBOUNCE_MS = 250;

/** A run of characters in the key or the title, half open: [start, end). */
export interface Mark {
  start: number;
  end: number;
}

export interface GoToRow {
  card: IssueCard;
  /** Which of the card's two strings the marks index into. */
  field: 'key' | 'title';
  marks: Mark[];
  /** The card is outside the tab's scope and listed only because its whole key
      was typed. False on every other row. */
  outOfScope: boolean;
}

export interface GoToResult {
  /** At most `GO_TO_LIMIT`. */
  rows: GoToRow[];
  /** How many issues matched, before the cap. */
  total: number;
}

const isAlnum = (ch: string) => /[a-z0-9]/i.test(ch);

/** Letters and digits only, lower case: the form a key and a query are compared in. */
export const squash = (text: string) => text.replace(/[^a-z0-9]/gi, '').toLowerCase();

/**
 * By number, then by project, so every project's issue 1 comes before any
 * issue 10. On a board with one project this is `compareIssueKeys`. A key that
 * does not split goes last, by text.
 */
export function compareByNumber(a: string, b: string): number {
  const left = splitKey(a);
  const right = splitKey(b);

  if (!left || !right) return compareIssueKeys(a, b);
  if (left.index !== right.index) return left.index - right.index;
  return left.project < right.project ? -1 : left.project > right.project ? 1 : 0;
}

/**
 * The marks for a key that matched by letters and digits: from the start to just
 * past the nth letter or digit, which carries them across the hyphen.
 */
export function prefixMarks(key: string, count: number): Mark[] {
  let seen = 0;
  for (let i = 0; i < key.length; i++) {
    if (isAlnum(key[i]) && ++seen === count) return [{ start: 0, end: i + 1 }];
  }
  return [{ start: 0, end: key.length }];
}

/** Every occurrence of every term in the title, sorted and merged. */
export function titleMarks(title: string, query: string): Mark[] {
  const lower = title.toLowerCase();
  const found: Mark[] = [];
  for (const term of query.toLowerCase().split(/\s+/).filter(Boolean)) {
    for (let at = lower.indexOf(term); at !== -1; at = lower.indexOf(term, at + 1)) {
      found.push({ start: at, end: at + term.length });
    }
  }

  found.sort((a, b) => a.start - b.start || a.end - b.end);
  const merged: Mark[] = [];
  for (const mark of found) {
    const last = merged[merged.length - 1];
    if (last && mark.start <= last.end) last.end = Math.max(last.end, mark.end);
    else merged.push({ ...mark });
  }
  return merged;
}

/**
 * The marks for a key a query names, or null when the query does not name it.
 * Digits alone are a number prefix; any other single word is a prefix of the key
 * on letters and digits alone. Several words name no key.
 */
export function keyMarks(key: string, query: string): Mark[] | null {
  const typed = query.trim();
  if (!typed || /\s/.test(typed)) return null;

  if (/^\d+$/.test(typed)) {
    const parts = splitKey(key);
    if (!parts || !String(parts.index).startsWith(typed)) return null;

    const tail = key.lastIndexOf('-') + 1;
    return [{ start: tail, end: tail + typed.length }];
  }

  const wanted = squash(typed);
  if (!wanted || !squash(key).startsWith(wanted)) return null;
  return prefixMarks(key, wanted.length);
}

/**
 * The issues a query could mean.
 *
 * - Empty or whitespace only: nothing.
 * - Digits alone are a number prefix: `11` is 11, 110, 111 and not 211. It is a
 *   rule on the whole query rather than on the key, because a project key may
 *   hold digits.
 * - A single word otherwise is a prefix of the key, on letters and digits alone
 *   and ignoring case, so `HA-3`, `ha-3` and `ha3` agree and none is HA-13.
 * - Then every issue whose title holds every typed word, in any order, that the
 *   keys have not already listed.
 *
 * All of that on the cards inside `scopeKeys` (none: every card). A single word
 * that is the whole key of a card outside them lists that card first, marked
 * `outOfScope`: `fil-1` is FIL-1 and not FIL-10.
 */
export function goToRows(all: IssueCard[], query: string, scopeKeys: string[] = []): GoToResult {
  const found = goToFound(all, query, scopeKeys);
  return { rows: found.slice(0, GO_TO_LIMIT), total: found.length };
}

/** Every row `goToRows` would draw, before the cap: the whole-key row outside the
    scope, then the keys, then the titles. */
export function goToFound(all: IssueCard[], query: string, scopeKeys: string[] = []): GoToRow[] {
  const typed = query.trim();
  if (!typed) return [];

  const cards = cardsInScope(all, scopeKeys);
  const outside: GoToRow[] = [];
  if (cards !== all && !/\s/.test(typed)) {
    const wanted = squash(typed);
    const inside = new Set(cards);
    const hit = wanted ? all.find((card) => !inside.has(card) && squash(card.key) === wanted) : undefined;
    if (hit) outside.push({ card: hit, field: 'key', marks: prefixMarks(hit.key, wanted.length), outOfScope: true });
  }

  const keyRows: GoToRow[] = [];
  const listed = new Set<string>();

  for (const card of cards) {
    const marks = keyMarks(card.key, typed);
    if (marks) keyRows.push({ card, field: 'key', marks, outOfScope: false });
  }

  keyRows.sort((a, b) => compareByNumber(a.card.key, b.card.key));
  for (const row of keyRows) listed.add(row.card.key);

  const titleRows: GoToRow[] = cards
    .filter((card) => !listed.has(card.key) && matchesText(card.title, typed))
    .sort((a, b) => compareIssueKeys(a.key, b.key))
    .map((card) => ({ card, field: 'title', marks: titleMarks(card.title, typed), outOfScope: false }));

  return [...outside, ...keyRows, ...titleRows];
}

/**
 * The key to highlight once the rows have changed: the same one if it is still
 * listed, otherwise the first row's, or null when there are none. Held by key
 * rather than by index so the board's refresh does not move the highlight to a
 * different issue.
 */
export function keepHighlight(rows: { key: string }[], key: string | null): string | null {
  if (key !== null && rows.some((r) => r.key === key)) return key;
  return rows[0]?.key ?? null;
}

/** What the lookup has said, for the status line. */
export interface GoToLookup {
  /** The server's answer left rows out past its limit. */
  more?: boolean;
  /** A request is out. */
  pending?: boolean;
}

/** The status line's left half. A lookup still out says so only while there is
    nothing to draw: rows already listed are real. */
export function goToStatus(
  query: string,
  shown: number,
  total: number,
  lookup: GoToLookup = {},
): string {
  if (!query.trim()) return 'type a key, a number, or words from a title';
  if (lookup.pending && shown === 0) return 'Loading…';
  if (total === 0) return 'no issue matches';
  if (shown < total) return `${shown} of ${total} — keep typing`;
  if (lookup.more) return `${shown}+ — keep typing`;
  return total === 1 ? '1 issue' : `${total} issues`;
}

/**
 * Where a card is on the board. A deferred column is not drawn, so a card in one
 * is `undrawn` even if the filter would hide it too; a card the filter is hiding
 * is `filtered`; anything else is `drawn` - including a card folded behind
 * `+ N waiting`, because the fold will open.
 *
 * `columns` is `boardColumns(statuses)`, the columns actually drawn.
 */
export function whereOnBoard(
  card: IssueCard,
  visibleKeys: Set<string>,
  columns: Status[],
): 'drawn' | 'filtered' | 'undrawn' {
  if (!columns.some((s) => s.id === card.statusId)) return 'undrawn';
  if (!visibleKeys.has(card.key)) return 'filtered';
  return 'drawn';
}

/** The trailing row's highlight key. `#` cannot occur in an issue key. */
export const SEARCH_ROW = '#search';

/** The console's full highlight order: every row's key, then `SEARCH_ROW` when
    the query is non-blank. */
export function entryKeys(rows: { key: string }[], query: string): string[] {
  const keys = rows.map((row) => row.key);
  if (searchPath(query) !== null) keys.push(SEARCH_ROW);
  return keys;
}
