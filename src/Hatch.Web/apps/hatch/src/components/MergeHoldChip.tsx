import type { MergeWork } from '../types';

/**
 * The issue's merge work, said to be held: a chip beside the pull request's,
 * naming why.
 *
 * Draws nothing for `merge`, `update`, `pending` or no judgement at all - the
 * absence of this one is the good news, since those are not a person's
 * problem yet.
 */
export function MergeHoldChip({ mergeWork }: { mergeWork: MergeWork | null | undefined }) {
  if (mergeWork?.kind !== 'hold') return null;

  return <span className="hatch-chip hatch-chip-merge-hold">{mergeWork.sentence}</span>;
}
