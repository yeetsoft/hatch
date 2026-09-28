import { conflictFileWords, conflictOf, conflictWords } from '../lib/mergeCheck';
import type { MergeCheck } from '../types';

/**
 * The issue's branch, when it has stopped merging with the trunk.
 *
 * Beside the pull request chip, because it is the same kind of fact: one small
 * thing about the delivery that belongs where the eye already goes for "what is
 * the state of this". It names the files, because "it conflicts" is a fact and
 * "it conflicts in these" is something a person can act on.
 *
 * Nothing at all for a clean branch, no branch, more than one branch and no
 * verdict yet - the same rule `PullRequestLink` follows for no pull request.
 * Those are the queue's to explain, and a row of "fine" chips is how a page
 * stops being read.
 */
export function MergeCheckChip({ checks }: { checks: MergeCheck[] | undefined }) {
  const conflict = conflictOf(checks);
  if (conflict === null) return null;

  return (
    <span className="hatch-conflict" title={conflict.files.join('\n')}>
      <span className="hatch-chip hatch-chip-conflict">{conflictWords(conflict)}</span>
      <code className="hatch-conflict-files">{conflictFileWords(conflict.files)}</code>
    </span>
  );
}
