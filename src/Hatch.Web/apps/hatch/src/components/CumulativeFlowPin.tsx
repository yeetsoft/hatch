import { bandStyle, breakdown, momentLabel } from '../lib/cumulativeFlow';
import type { CumulativeFlow, CumulativeFlowPoint, Status } from '../types';

/**
 * The pinned moment, beside (or under, on a phone) the chart: the same rows
 * the hover card draws, from props alone so it has no opinion of its own
 * about how the pin got set. `aria-live="polite"` so a keyboard step is
 * announced without a focus change.
 */
export function CumulativeFlowPin({
  point,
  columns,
  statuses,
  step,
  locale,
}: {
  point: CumulativeFlowPoint | null;
  columns: number[];
  statuses: Status[];
  step: CumulativeFlow['step'];
  locale?: string;
}) {
  if (point === null) {
    return (
      <aside className="hatch-cumulative-flow-pin" aria-live="polite">
        <p className="text-muted">Click the chart to pin a moment.</p>
      </aside>
    );
  }

  const { rows, total } = breakdown(point, columns, statuses);

  return (
    <aside className="hatch-cumulative-flow-pin" aria-live="polite">
      <div className="hatch-cumulative-flow-card-moment">{momentLabel(point.at, step, locale)}</div>

      {rows.map((row) => (
        <div key={row.columnId} className="hatch-cumulative-flow-card-row">
          <span className="hatch-cumulative-flow-swatch" style={bandStyle(statuses, row.columnId)} />
          {row.name} {row.count}
        </div>
      ))}

      <div className="hatch-cumulative-flow-card-total">Total {total}</div>
    </aside>
  );
}
