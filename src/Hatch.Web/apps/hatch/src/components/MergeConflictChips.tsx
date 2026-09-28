import { conflictTitle, conflictWords, conflictedChecks } from '../lib/mergeCheck';
import type { MergeCheck } from '../types';

/**
 * The issue's branch, said to conflict with the trunk: a chip beside the pull
 * request's, one per repository that does, naming the files on hover.
 *
 * Draws nothing for a clean verdict, no branch, more than one, or no verdict at
 * all - a chip for every branch that is fine would be a chip on every page in
 * review, and the absence of this one is the good news.
 */
export function MergeConflictChips({ checks }: { checks: readonly MergeCheck[] }) {
  const conflicted = conflictedChecks(checks);
  const several = conflicted.length > 1;

  return (
    <>
      {conflicted.map((c) => (
        <span key={c.canonical} className="hatch-chip hatch-chip-conflict" title={conflictTitle(c, several)}>
          {conflictWords(c, several)}
        </span>
      ))}
    </>
  );
}
