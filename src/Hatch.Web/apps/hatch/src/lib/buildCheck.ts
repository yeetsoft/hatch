/* Drawing what a runner found when it read the build on an issue's branch. */

import type { BuildCheck } from '../types';

/** The verdict that is worth a chip. A build that passed, is still running or
    never ran is nothing to draw: a chip for every green build would be a chip
    on every page in review. */
export const FAILED = 'failed';

/** A build still running, which counts as failing here once it carries a
    check that has already failed - the rest concluding cannot un-fail it. */
export const PENDING = 'pending';

/**
 * The verdicts that say the build failed, one per repository, in the order the
 * server sent them.
 *
 * An issue's build failed if any of its repositories' did, so a passing one
 * beside a failing one is dropped here rather than drawn as a second,
 * reassuring chip. No verdict at all is an empty list, and so is a passing
 * one - and so is one still running with nothing failed yet, but one still
 * running that already carries a failed check is not: a check that has failed
 * counts while the rest are still running.
 */
export function failedBuilds(checks: readonly BuildCheck[] | null | undefined): BuildCheck[] {
  return (checks ?? []).filter((c) => c.verdict === FAILED || (c.verdict === PENDING && c.failing.length > 0));
}

/**
 * `Build failing: api, CI`, or `Build failing, still running: api, CI` while
 * the rest of the build has not yet concluded.
 *
 * Which repository it is said of is added only when more than one fails,
 * because with one there is nothing to tell apart.
 */
export function buildWords(check: BuildCheck, several: boolean = false): string {
  const names = check.failing.map((f) => f.name).join(', ');
  const where = several ? ` in ${check.canonical}` : '';
  const still = check.verdict === PENDING ? ', still running' : '';

  return `Build failing${still}: ${names}${where}`;
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
