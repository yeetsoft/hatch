import { meterText, tightness } from '../lib/wip';
import type { WipRun } from '../lib/wip';
import type { Wip } from '../types';

/**
 * The band across the WIP section's columns: one per run, so a section split
 * by a column outside it reads as two bands sharing the same meter. Drawn
 * nothing where there is no section, or nothing of it survived onto this
 * board's drawn columns - see lib/wip.ts's `runs`.
 */
export function WipBands({ runs, section, load }: { runs: WipRun[]; section: Wip | null; load: number }) {
  if (!section || runs.length === 0) return null;

  const tint = tightness(load, section.limit);
  const text = meterText(section, load);
  const previewing = load !== section.load;

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
