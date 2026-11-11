import { MEASURES, peakLabel, type ActivityMeasure } from '../lib/activityGraph';
import { calendarWeeks } from '../lib/activityCalendar';
import type { ActivityReport } from '../types';

/** One day's cell, in the SVG's own units. The cell is inset inside its slot,
    so the gap between cells is arithmetic rather than a stroke. */
const CELL = 10;
const INSET = 1;

/**
 * The rate of work over a long range, as a calendar: one column a week,
 * Monday to Sunday down it, each cell shaded by how much of the chosen
 * measure that day holds.
 *
 * Hand-cut SVG in the house's own tokens, as `ActivityGraph` is - no
 * charting dependency. Everything that could be wrong is in
 * `lib/activityCalendar.ts` and tested there; what is here places what that
 * returns. `ActivityGraph` is left untouched: the page draws one of the two,
 * never both, so this carries its own measure buttons rather than sharing
 * them.
 *
 * **A day outside the window is a transparent cell with no title; a present
 * day with nothing in it is still shaded, at the palest step, and still
 * carries a title** - a cell reads as "nothing happened" and never as a gap
 * in the grid.
 */
export function ActivityCalendar({
  report,
  measure,
  onMeasure,
  locale,
}: {
  report: ActivityReport;
  measure: ActivityMeasure;
  onMeasure: (measure: ActivityMeasure) => void;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
}) {
  const weeks = calendarWeeks(report, measure, locale);
  const width = Math.max(1, weeks.length) * CELL;
  const height = 7 * CELL;

  return (
    <div className="hatch-activity hatch-calendar">
      <div className="hatch-activity-head">
        <div className="hatch-activity-measures" role="group" aria-label="Measure">
          {MEASURES.map((option) => (
            <button
              key={option.key}
              type="button"
              className={`hatch-activity-measure${measure === option.key ? ' active' : ''}`}
              aria-pressed={measure === option.key}
              onClick={() => onMeasure(option.key)}
            >
              {option.label}
            </button>
          ))}
        </div>

        <span className="hatch-activity-peak text-muted">{peakLabel(report, measure)}</span>
      </div>

      <svg
        className="hatch-calendar-plot"
        viewBox={`0 0 ${width} ${height}`}
        preserveAspectRatio="xMinYMin meet"
        role="img"
        aria-label={peakLabel(report, measure)}
      >
        {weeks.map((column, w) =>
          column.map((cell, d) =>
            cell === null ? (
              <rect
                key={`${w}-${d}`}
                className="hatch-calendar-pad"
                x={w * CELL}
                y={d * CELL}
                width={CELL - INSET}
                height={CELL - INSET}
              />
            ) : (
              <rect
                key={`${w}-${d}`}
                className={`hatch-calendar-step-${cell.step}`}
                x={w * CELL}
                y={d * CELL}
                width={CELL - INSET}
                height={CELL - INSET}
              >
                <title>{cell.label}</title>
              </rect>
            ),
          ),
        )}
      </svg>
    </div>
  );
}
