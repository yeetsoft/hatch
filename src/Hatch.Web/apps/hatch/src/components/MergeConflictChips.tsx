import { useState } from 'react';
import { conflictTitle, conflictWords, conflictedChecks } from '../lib/mergeCheck';
import type { MergeCheck } from '../types';

/**
 * The issue's branch, said to conflict with the trunk: a chip beside the pull
 * request's, one per repository that does, naming the files behind a press.
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
        <ConflictChip key={c.canonical} check={c} several={several} />
      ))}
    </>
  );
}

function ConflictChip({ check, several }: { check: MergeCheck; several: boolean }) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="hatch-chip hatch-chip-conflict"
        title={conflictTitle(check, several)}
        aria-expanded={open}
        onClick={() => setOpen((o) => !o)}
      >
        {conflictWords(check, several)}
      </button>
      {open && (
        <ul className="hatch-chip-files">
          {check.files.map((f) => (
            <li key={f}>{f}</li>
          ))}
        </ul>
      )}
    </>
  );
}
