/* Drawing what a runner found when it asked git whether an issue's branch still
   merges with the trunk. */

import type { MergeCheck } from '../types';

/** How many file names the chip spells out before it says how many more. */
export const CONFLICT_FILES_SHOWN = 5;

/** What one issue's conflict comes to, across every repository it conflicts in. */
export interface ConflictSummary {
  /** The trunk the first conflicted repository was checked against. Repositories
      that were checked against different trunks are rare enough that naming
      one is better than naming a list. */
  trunk: string;
  /** Every conflicted file, once, in the order the server gave them. */
  files: string[];
}

/**
 * The conflict, or null when nothing conflicts.
 *
 * Only `conflicted` says anything: a clean verdict, no branch, more than one
 * branch and no verdict at all are all nothing to draw on the issue page. The
 * queue explains those; the page has no use for a chip that says "fine".
 */
export function conflictOf(checks: MergeCheck[] | undefined): ConflictSummary | null {
  const conflicted = (checks ?? []).filter((c) => c.verdict === 'conflicted');
  if (conflicted.length === 0) return null;

  const files = [...new Set(conflicted.flatMap((c) => c.files))];
  return { trunk: conflicted[0].trunk, files };
}

/** `Conflicts with main: 3 files`, `Conflicts with main: 1 file`. */
export function conflictWords(conflict: ConflictSummary): string {
  const n = conflict.files.length;
  return `Conflicts with ${conflict.trunk}: ${n} ${n === 1 ? 'file' : 'files'}`;
}

/** The files, cut to what a line can hold: `a.txt, b.txt and 3 more`. */
export function conflictFileWords(files: string[], shown: number = CONFLICT_FILES_SHOWN): string {
  if (files.length <= shown) return files.join(', ');
  return `${files.slice(0, shown).join(', ')} and ${files.length - shown} more`;
}

/** One side of a `merge_check_changed` event: the verdict, and the files it named. */
function sideWords(side: unknown): string {
  if (side === null || side === undefined || typeof side !== 'object') return 'none';

  const { verdict, files } = side as { verdict?: unknown; files?: unknown };
  const name = typeof verdict === 'string' ? verdict : 'none';
  const list = Array.isArray(files) ? files.filter((f): f is string => typeof f === 'string') : [];

  return list.length > 0 ? `${name} (${conflictFileWords(list)})` : name;
}

/** The trail's line for a verdict that changed: `clean → conflicted (a.txt)`. */
export function mergeCheckEventWords(payload: { from?: unknown; to?: unknown } | null): string {
  return `${sideWords(payload?.from)} → ${sideWords(payload?.to)}`;
}
