/* Drawing what a runner found when it read the build on an issue's branch. */

import type { BuildCheck } from '../types';

/** The verdict that is worth a chip. A build that passed, is still running or
    never ran is nothing to draw: a chip for every green build would be a chip
    on every page in review. */
export const FAILED = 'failed';

/**
 * The verdicts that say the build failed, one per repository, in the order the
 * server sent them.
 *
 * An issue's build failed if any of its repositories' did, so a passing one
 * beside a failing one is dropped here rather than drawn as a second,
 * reassuring chip. No verdict at all is an empty list, and so is a passing one.
 */
export function failedBuilds(checks: readonly BuildCheck[] | null | undefined): BuildCheck[] {
  return (checks ?? []).filter((c) => c.verdict === FAILED);
}

/**
 * `Build failing: api, CI`.
 *
 * Which repository it is said of is added only when more than one fails,
 * because with one there is nothing to tell apart.
 */
export function buildWords(check: BuildCheck, several: boolean = false): string {
  const names = check.failing.map((f) => f.name).join(', ');
  const where = several ? ` in ${check.canonical}` : '';

  return `Build failing: ${names}${where}`;
}

/**
 * What the chip's `title` says: the sentence, then the sha it is about.
 *
 * The sha is what tells somebody whether this is the build on the branch as it
 * stands or on something the branch has since moved past.
 */
export function buildTitle(check: BuildCheck, several: boolean = false): string {
  return [buildWords(check, several), `on ${check.sha.slice(0, 10)}`].join('\n');
}
