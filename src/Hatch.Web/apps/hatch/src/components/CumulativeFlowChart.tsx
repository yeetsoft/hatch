import { useRef, useState } from 'react';
import type { PointerEvent as ReactPointerEvent } from 'react';
import { bandPolygon, bandStyle, bands, breakdown, momentLabel, peakLabel, pointIndexAt, slots } from '../lib/cumulativeFlow';
import { CHART_UNITS, ticks, todayX, type Scale } from '../lib/timeline';
import type { CumulativeFlow, Status } from '../types';

/** Own to this component, as `SpendGraph.tsx`'s is. */
const HEIGHT = 100;

/** The hover card's own footprint, matching its CSS `width` exactly - a fixed
    width, not `max-width`, is what keeps the flip math below exact. */
const CARD_WIDTH = 220;
const GAP = 12;

/**
 * How much work sat in each board column over time: one band per column,
 * stacked bottom-up with the board's last column at the baseline, drawn as
 * one polygon each in the column's own colour.
 *
 * Hand-cut SVG in the house's own tokens - the app carries no charting
 * dependency and this does not add one; `SpendGraph` is the precedent.
 * Everything that could be wrong is in `lib/cumulativeFlow.ts` and tested
 * there, and what is here places what that returns.
 */
export function CumulativeFlowChart({
  answer,
  statuses,
  scale,
  offsetMinutes,
  now,
  locale,
}: {
  answer: CumulativeFlow;
  statuses: Status[];
  scale: Scale;
  offsetMinutes: number;
  now: Date;
  locale?: string;
}) {
  const series = bands(answer, scale);
  const today = todayX(scale, now);
  const slotted = slots(answer.points, scale);
  const svgRef = useRef<SVGSVGElement>(null);
  const [pointer, setPointer] = useState<{ index: number; x: number; y: number } | null>(null);

  const onPointerMove = (event: ReactPointerEvent<SVGSVGElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const paneX = ((event.clientX - rect.left) / rect.width) * CHART_UNITS;
    const index = pointIndexAt(answer.points, scale, paneX);

    setPointer(index === null ? null : { index, x: event.clientX, y: event.clientY });
  };

  const hovered = pointer ? answer.points[pointer.index] : null;
  const hoveredBreakdown = hovered ? breakdown(hovered, answer.columns, statuses) : null;
  const near =
    pointer !== null &&
    svgRef.current !== null &&
    pointer.x >= svgRef.current.getBoundingClientRect().right - CARD_WIDTH - GAP;

  return (
    <div className="hatch-cumulative-flow">
      <div className="hatch-cumulative-flow-legend">
        {answer.columns.map((columnId) => {
          const name = statuses.find((status) => status.id === columnId)?.name ?? 'Unknown';

          return (
            <span key={columnId} className="hatch-cumulative-flow-legend-item">
              <span className="hatch-cumulative-flow-swatch" style={bandStyle(statuses, columnId)} />
              {name}
            </span>
          );
        })}

        {/* Pushed to the far end, as the spend graph's own peak label is -
            the rule that does it is layout-only and not spend-specific. */}
        <span className="hatch-spend-peak text-muted">{peakLabel(answer.points)}</span>
      </div>

      <div className="hatch-timeline-pane">
        <div className="hatch-timeline-track">
          <div className="hatch-timeline-ruler">
            {ticks(scale, offsetMinutes, locale).map((tick) => (
              <span key={tick.x} className="hatch-timeline-tick" style={{ left: `${tick.x}%` }}>
                {tick.label}
              </span>
            ))}
          </div>

          <svg
            ref={svgRef}
            className="hatch-cumulative-flow-plot"
            viewBox={`0 0 ${CHART_UNITS} ${HEIGHT}`}
            preserveAspectRatio="none"
            role="img"
            aria-label={peakLabel(answer.points)}
            onPointerMove={onPointerMove}
            onPointerLeave={() => setPointer(null)}
          >
            {series.map((band, i) => (
              <polygon
                key={answer.columns[answer.columns.length - 1 - i]}
                className="hatch-timeline-segment"
                points={bandPolygon(band, HEIGHT)}
                style={bandStyle(statuses, answer.columns[answer.columns.length - 1 - i])}
              />
            ))}

            {answer.points.map((point, i) => (
              <rect
                key={point.at}
                className="hatch-spend-slot"
                x={slotted[i].x}
                y={0}
                width={slotted[i].width}
                height={HEIGHT}
              />
            ))}
          </svg>

          {pointer && hovered && hoveredBreakdown && (
            <div
              className="hatch-cumulative-flow-card"
              aria-hidden
              style={{ top: pointer.y + GAP, left: near ? pointer.x - GAP - CARD_WIDTH : pointer.x + GAP }}
            >
              <div className="hatch-cumulative-flow-card-moment">{momentLabel(hovered.at, answer.step, locale)}</div>

              {hoveredBreakdown.rows.map((row) => (
                <div key={row.columnId} className="hatch-cumulative-flow-card-row">
                  <span className="hatch-cumulative-flow-swatch" style={bandStyle(statuses, row.columnId)} />
                  {row.name} {row.count}
                </div>
              ))}

              <div className="hatch-cumulative-flow-card-total">Total {hoveredBreakdown.total}</div>
            </div>
          )}

          {today !== null && <div className="hatch-timeline-today" style={{ left: `${today}%` }} aria-hidden />}
        </div>
      </div>
    </div>
  );
}
