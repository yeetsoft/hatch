/* A runner's column rule, as the settings card draws and edits it.

   Pure, no React, so a press - which the test suite cannot click - is a function
   that can be called. The server holds the rule (Dispatch.cs reads it on every
   dispatch); this only says which columns the card lists, which of them a rule
   leaves lit, and how the rule reads as a sentence. */

import type { RunnerStatusMode, Status } from '../types';

/** The three choices, in the order the card offers them. */
export const COLUMN_MODES: { mode: RunnerStatusMode; label: string }[] = [
  { mode: 'all', label: 'Every column' },
  { mode: 'except', label: 'Every column except' },
  { mode: 'only', label: 'Only these' },
];

/**
 * The columns a runner can be told to work or skip: the ones that are neither
 * terminal nor deferred, in board order. A runner is never dispatched out of a
 * shipped or shelved column, so listing one would be a choice with no effect.
 * Returns a copy; `statuses` is the caller's.
 */
export const workableColumns = (statuses: Status[]): Status[] =>
  statuses.filter((s) => !s.isTerminal && !s.isDeferred).sort((a, b) => a.sortOrder - b.sortOrder);

/**
 * Whether the runner works a column, given the rule and whether the row is
 * ticked. Under `except` a ticked column is the one it skips; under `only` a
 * ticked column is the one it works. Under `all` every column is lit.
 */
export function columnLit(mode: RunnerStatusMode, ticked: boolean): boolean {
  if (mode === 'except') return !ticked;
  if (mode === 'only') return ticked;
  return true;
}

/** `ids` with `id` ticked if it was not and unticked if it was. */
export const toggleColumn = (ids: number[], id: number): number[] =>
  ids.includes(id) ? ids.filter((i) => i !== id) : [...ids, id];

/** **All**: every listed column ticked. */
export const allColumns = (columns: Status[]): number[] => columns.map((c) => c.id);

/** **None**: nothing ticked. */
export const noColumns = (): number[] => [];

/** A row's **only** press: that column ticked and no other. */
export const onlyColumn = (id: number): number[] => [id];

/** Whether two id lists name the same columns, in whatever order. */
export function sameColumnIds(a: number[], b: number[]): boolean {
  const left = new Set(a);
  const right = new Set(b);
  return left.size === right.size && [...left].every((id) => right.has(id));
}

/** The sentence under the list, saying what nothing ticked means. Empty under
    `all`, which draws no list. */
export function columnsHint(mode: RunnerStatusMode): string {
  if (mode === 'only') return 'Nothing ticked works nothing. Binds on the board\'s very next dispatch read.';
  if (mode === 'except') return 'Nothing ticked works everything. Binds on the board\'s very next dispatch read.';
  return '';
}

/**
 * The rule as the read-only line says it: `every column`, `every column except
 * In Progress, Testing`, or `only Breakdown, In Review`. Names come in board
 * order; an id the list does not draw (a column since made terminal or deferred)
 * is skipped, and an `only` left with no name reads `no column`.
 */
export function columnsWords(mode: RunnerStatusMode, ids: number[], statuses: Status[]): string {
  if (mode === 'all') return 'every column';
  const named = workableColumns(statuses)
    .filter((c) => ids.includes(c.id))
    .map((c) => c.name);
  if (mode === 'only') return named.length === 0 ? 'no column' : `only ${named.join(', ')}`;
  return named.length === 0 ? 'every column' : `every column except ${named.join(', ')}`;
}
