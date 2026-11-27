/* What a server lookup lays over a picker's own candidates: what it adds, what
   the popup says while an answer is out, and where the highlight goes once one
   lands.

   In a module with no React in it for the reason lib/issuePicker.ts is: the
   decision goes in `lib/` and is tested, and the hook beside this one
   (lib/usePickerLookup.ts) is wiring with none in it. */

import type { GoToRowDto, IssueCard } from '../types';

/** The key, the title and the type - everything a picker row draws, whether it
    came from a card the page holds or a row the server answered with. */
export interface PickedRow {
  key: string;
  title: string;
  type: string;
}

/** `PickedRow` out of either source. */
export function rowOf(source: IssueCard | GoToRowDto): PickedRow {
  return { key: source.key, title: source.title, type: source.type };
}

/** The last lookup request to end, or null before the first has. `failed` is a
    request that did not come back; the query it was asked about is kept all
    the same, so nothing is asked again for it until the query moves on. */
export interface PickerLookupFinished {
  /** The settled, trimmed query it was asked about. */
  query: string;
  rows: GoToRowDto[];
  failed: boolean;
}

/**
 * The page's rows, then the server's: the page's own first, in the order
 * `pickerRows` already put them in, and then whatever the lookup found that
 * the page did not - in the server's own order.
 *
 * Dropped from the server's rows: any marked `outOfScope`, any `accept`
 * refuses, and any whose key is already among the page's own. That last
 * check is against the page's rows, not every candidate the page holds: a
 * card the typed text does not match, but the server's own stemmed match
 * does, is still listed once, from the server.
 *
 * Never run back through `matchesPickerQuery` - the server has already
 * matched it its own way, which a card's own text may not even contain.
 */
export function mergePickerRows(
  pageRows: PickedRow[],
  looked: GoToRowDto[],
  accept: (row: PickedRow) => boolean,
): PickedRow[] {
  const have = new Set(pageRows.map((row) => row.key));
  const extra = looked
    .filter((row) => !row.outOfScope)
    .map(rowOf)
    .filter((row) => !have.has(row.key) && accept(row));
  return [...pageRows, ...extra];
}

/** What to draw for the lookup: its rows, and whether an answer is still out. */
export interface LookupView {
  rows: GoToRowDto[];
  looking: boolean;
}

/**
 * What the lookup has to show for `typed`.
 *
 * Nothing typed asks nothing, so there is nothing to wait for either. A
 * finished answer to exactly this text is drawn - none of its rows, if that
 * request failed. Anything else - an answer that was for older text, or none
 * that has come back yet - is still being asked about: a stale answer is
 * never drawn or takeable.
 */
export function lookupView(typed: string, finished: PickerLookupFinished | null): LookupView {
  const trimmed = typed.trim();
  if (!trimmed) return { rows: [], looking: false };
  if (finished && finished.query === trimmed) return { rows: finished.failed ? [] : finished.rows, looking: false };
  return { rows: [], looking: true };
}

/**
 * What the popup says in place of its list, or null to draw one.
 *
 * Without a lookup this is the rule the picker always had: `empty` when there
 * are no candidates at all, `nothing` when there are but none of them match
 * what was typed.
 *
 * With one: `looking` while an answer is out, so a slow link never reads as
 * "nothing matches" partway through finding something; `empty` only when
 * nothing is typed and the page itself holds no candidates, since a lookup
 * might still find something past them; `nothing` once something is typed,
 * nothing is still out, and no row is left to draw.
 */
export function pickerNote(args: {
  lookup: boolean;
  /** How many candidates the page handed the picker, before any filter. */
  candidates: number;
  /** How many rows are left to draw, page and lookup together, excluding the
      pinned clear row. */
  rows: number;
  typed: string;
  looking: boolean;
}): 'empty' | 'looking' | 'nothing' | null {
  const { lookup, candidates, rows, typed, looking } = args;
  const hasTyped = typed.trim().length > 0;

  if (!lookup) {
    if (candidates === 0) return 'empty';
    return rows === 0 ? 'nothing' : null;
  }

  if (looking) return 'looking';
  if (!hasTyped) return candidates === 0 ? 'empty' : null;
  return rows === 0 ? 'nothing' : null;
}

/**
 * Where the highlight settles once the row count changes under it, rather
 * than by typing - a lookup's answer landing, which `typed()` cannot see
 * coming and so cannot place the highlight on.
 *
 * A null highlight (nothing matched when it was last set) moves onto the
 * first row past the clear row once one exists, and stays null otherwise. An
 * index already on a row is left alone unless the list no longer reaches it,
 * in which case it clamps onto the last row still there.
 */
export function settleHighlight(highlight: number | null, rowCount: number, offset: number): number | null {
  if (highlight === null) return rowCount > offset ? offset : null;
  if (highlight <= rowCount - 1) return highlight;
  return rowCount > 0 ? rowCount - 1 : null;
}
