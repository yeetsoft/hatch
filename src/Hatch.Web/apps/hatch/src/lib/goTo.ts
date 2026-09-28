/* The console's decisions: what a query finds, in what order, what the status
   line says, and where the card is on the board.

   In a module with no React in it for the reason lib/issuePicker.ts is: the rule
   goes in `lib/` and is tested, and the layout and the keys are looked at in a
   browser. What is left in OmniBar and BoardPage is state and events.

   Keys come first, then titles. Somebody who types a number wants the issue with
   that number, and a title that happens to contain it must not take its place. */

import { compareIssueKeys, matchesText, splitKey } from './issuePicker';
import type { IssueCard, Status } from '../types';

/**
 * How many rows the console draws. The board holds every issue in the house, and
 * `1` matches a tenth of them: hundreds of rows is a page nobody reads, and the
 * status line says `50 of 112 — keep typing` instead.
 */
export const GO_TO_LIMIT = 50;

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
}

export interface GoToResult {
  /** At most `GO_TO_LIMIT`. */
  rows: GoToRow[];
  /** How many issues matched, before the cap. */
  total: number;
}

const isAlnum = (ch: string) => /[a-z0-9]/i.test(ch);

/** Letters and digits only, lower case: the form a key and a query are compared in. */
const squash = (text: string) => text.replace(/[^a-z0-9]/gi, '').toLowerCase();

/**
 * By number, then by project, so every project's issue 1 comes before any
 * issue 10. On a board with one project this is `compareIssueKeys`. A key that
 * does not split goes last, by text.
 */
function compareByNumber(a: string, b: string): number {
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
function prefixMarks(key: string, count: number): Mark[] {
  let seen = 0;
  for (let i = 0; i < key.length; i++) {
    if (isAlnum(key[i]) && ++seen === count) return [{ start: 0, end: i + 1 }];
  }
  return [{ start: 0, end: key.length }];
}

/** Every occurrence of every term in the title, sorted and merged. */
function titleMarks(title: string, query: string): Mark[] {
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
 */
export function goToRows(cards: IssueCard[], query: string): GoToResult {
  const typed = query.trim();
  if (!typed) return { rows: [], total: 0 };

  const keyRows: GoToRow[] = [];
  const listed = new Set<string>();

  if (/^\d+$/.test(typed)) {
    for (const card of cards) {
      const parts = splitKey(card.key);
      if (!parts || !String(parts.index).startsWith(typed)) continue;

      const tail = card.key.lastIndexOf('-') + 1;
      keyRows.push({ card, field: 'key', marks: [{ start: tail, end: tail + typed.length }] });
    }
  } else if (!/\s/.test(typed)) {
    const wanted = squash(typed);
    if (wanted) {
      for (const card of cards) {
        if (!squash(card.key).startsWith(wanted)) continue;
        keyRows.push({ card, field: 'key', marks: prefixMarks(card.key, wanted.length) });
      }
    }
  }

  keyRows.sort((a, b) => compareByNumber(a.card.key, b.card.key));
  for (const row of keyRows) listed.add(row.card.key);

  const titleRows: GoToRow[] = cards
    .filter((card) => !listed.has(card.key) && matchesText(card.title, typed))
    .sort((a, b) => compareIssueKeys(a.key, b.key))
    .map((card) => ({ card, field: 'title', marks: titleMarks(card.title, typed) }));

  const all = [...keyRows, ...titleRows];
  return { rows: all.slice(0, GO_TO_LIMIT), total: all.length };
}

/**
 * The key to highlight once the rows have changed: the same one if it is still
 * listed, otherwise the first row's, or null when there are none. Held by key
 * rather than by index so the board's refresh does not move the highlight to a
 * different issue.
 */
export function keepHighlight(rows: GoToRow[], key: string | null): string | null {
  if (key !== null && rows.some((r) => r.card.key === key)) return key;
  return rows[0]?.card.key ?? null;
}

/** The status line's left half. */
export function goToStatus(query: string, shown: number, total: number): string {
  if (!query.trim()) return 'type a key, a number, or words from a title';
  if (total === 0) return 'no issue matches';
  if (shown < total) return `${shown} of ${total} — keep typing`;
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
