import { meterText, tightest } from '../lib/wip';
import type { WipRun } from '../lib/wip';
import type { Wip } from '../types';

/**
 * The band across the WIP section's columns: one per run, so a section split
 * by a column outside it reads as two bands sharing the same meter. Drawn
 * nothing where there is no section, nothing of it survived onto this board's
 * drawn columns (see lib/wip.ts's `runs`), or no slice has a limit.
 */
export function WipBands({ runs, section, loads }: { runs: WipRun[]; section: Wip | null; loads: number[] }) {
  const tint = section ? tightest(section, loads) : null;
  if (!section || runs.length === 0 || tint === null) return null;

  const text = meterText(section, loads);
  const previewing = section.slices.some((slice, i) => slice.limit !== null && loads[i] !== slice.load);

  return (
    <>
      {runs.map((run) => (
        <div
          key={run.start}
          className={`hatch-wip-band hatch-wip-${tint}${previewing ? ' hatch-wip-preview' : ''}`}
          style={{ gridColumn: `${run.start + 1} / span ${run.statusIds.length}` }}
        >
          {text}
        </div>
      ))}
    </>
  );
}
