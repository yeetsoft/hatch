/* Drawing what a runner found when it merged an issue's branch against the
   trunk. */

import type { MergeCheck } from '../types';

/** The verdict that is worth a chip. The others - a clean merge, no branch, more
    than one - are all the same thing to draw, which is nothing: a chip for a
    branch that merges cleanly would be a chip on every page in review. */
export const CONFLICTED = 'conflicted';

/**
 * The verdicts that say the branch conflicts, one per repository, in the order
 * the server sent them.
 *
 * An issue conflicts if any of its repositories does, so a clean one beside a
 * conflicted one is dropped here rather than drawn as a second, reassuring
 * chip. No verdict at all is an empty list, and so is a clean one.
 */
export function conflictedChecks(checks: readonly MergeCheck[] | null | undefined): MergeCheck[] {
  return (checks ?? []).filter((c) => c.verdict === CONFLICTED);
}

/**
 * `Conflicts with main: 3 files`.
 *
 * The trunk is named as the runner reported it, and nothing here knows what one
 * is called: a label that only read `main` would be a fact about exactly one
 * installation. Which repository it is said of is added only when more than one
 * conflicts, because with one there is nothing to tell apart.
 */
export function conflictWords(check: MergeCheck, several: boolean = false): string {
  const files = check.files.length === 1 ? '1 file' : `${check.files.length} files`;
  const where = several ? ` in ${check.canonical}` : '';

  return `Conflicts with ${check.trunk}: ${files}${where}`;
}

/**
 * What the chip's `title` says: the sentence, then every file, one to a line.
 *
 * Here and not in the chip's text because a conflict can name hundreds of
 * paths and the chip lives on the page's meta line; the whole list is one
 * hover away and one selection long.
 */
export function conflictTitle(check: MergeCheck, several: boolean = false): string {
  return [conflictWords(check, several), ...check.files].join('\n');
}
