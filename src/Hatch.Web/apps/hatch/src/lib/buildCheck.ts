/* Drawing what a runner read of the build on an issue's branch. */

import type { BuildCheck, FailingCheck } from '../types';

/** The verdict that is worth a chip. A build that passed, is still running or
    has no checks is all the same thing to draw, which is nothing: a chip for a
    build that is fine would be a chip on every page in review. */
export const FAILED = 'failed';

/**
 * The verdicts that say the build fails, one per repository, in the order the
 * server sent them.
 *
 * An issue's build fails if any of its repositories does, so a passing one
 * beside a failing one is dropped here rather than drawn as a second,
 * reassuring chip. No verdict at all is an empty list, and so is a passing one.
 */
export function failedBuilds(checks: readonly BuildCheck[] | null | undefined): BuildCheck[] {
  return (checks ?? []).filter((c) => c.verdict === FAILED);
}

/**
 * The address to link a failing check to, or null.
 *
 * The server keeps only an http or https address, and this asks again: a key
 * writes these and the page turns them into anchors, so a `javascript:` value
 * must not reach an `href` on the strength of one side's check.
 */
export function checkLink(check: FailingCheck): string | null {
  if (check.url === null) return null;

  try {
    const { protocol } = new URL(check.url);
    return protocol === 'https:' || protocol === 'http:' ? check.url : null;
  } catch {
    return null;
  }
}

/**
 * `Build fails: build, test`.
 *
 * The checks are named as the repository's CI named them, and nothing here
 * knows what one is called. Which repository it is said of is added only when
 * more than one fails, because with one there is nothing to tell apart.
 */
export function buildWords(check: BuildCheck, several: boolean = false): string {
  const where = several ? ` in ${check.canonical}` : '';

  return `Build fails: ${check.failing.map((f) => f.name).join(', ')}${where}`;
}

/** What a chip's `title` says: the sentence, then every check, one to a line. */
export function buildTitle(check: BuildCheck, several: boolean = false): string {
  return [buildWords(check, several), ...check.failing.map((f) => f.name)].join('\n');
}
