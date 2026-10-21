import { useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from 'react';
import {
  bandPolygon,
  bandStyle,
  bands,
  breakdown,
  momentLabel,
  peakLabel,
  pinAfter,
  pointIndexAt,
  slots,
} from '../lib/cumulativeFlow';
import type { PinAction } from '../lib/cumulativeFlow';
import { CHART_UNITS, ticks, todayX, xOf, type Scale } from '../lib/timeline';
import { CumulativeFlowPin } from './CumulativeFlowPin';
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
 *
 * Beside the chart sits a pinned moment: a press or a drag on the plot (or
 * on the pin line itself) moves it, ← / → with the plot focused steps it one
 * point, and a new answer clears it. `pinAfter` is the only place the rules
 * for any of that live; this component just calls it.
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
  const [pin, setPin] = useState<number | null>(null);
  const [dragging, setDragging] = useState(false);

  // The answer object is new on every load; this is React's own
  // adjust-state-during-render pattern, not an effect, so the pin never
  // flashes the old point before clearing.
  const [seen, setSeen] = useState(answer);
  if (seen !== answer) {
    setSeen(answer);
    setPin((p) => pinAfter(p, answer.points.length, { type: 'answer' }));
    setDragging(false);
  }

  const apply = (action: PinAction) => setPin((p) => pinAfter(p, answer.points.length, action));

  const indexAt = (clientX: number): number | null => {
    if (!svgRef.current) return null;

    const rect = svgRef.current.getBoundingClientRect();
    const paneX = ((clientX - rect.left) / rect.width) * CHART_UNITS;

    return pointIndexAt(answer.points, scale, paneX);
  };

  const onPress = (event: ReactPointerEvent) => {
    svgRef.current?.setPointerCapture(event.pointerId);
    setDragging(true);
    apply({ type: 'press', index: indexAt(event.clientX) });
  };

  const onPointerMove = (event: ReactPointerEvent<SVGSVGElement>) => {
    const index = indexAt(event.clientX);

    setPointer(index === null ? null : { index, x: event.clientX, y: event.clientY });
    if (dragging) apply({ type: 'move', index });
  };

  const stopDragging = () => setDragging(false);

  const onKeyDown = (event: ReactKeyboardEvent<SVGSVGElement>) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      apply({ type: 'step', delta: -1 });
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      apply({ type: 'step', delta: 1 });
    }
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

      <div className="hatch-cumulative-flow-body">
        <div className="hatch-timeline-pane">
          <div className="hatch-timeline-track">
            <div className="hatch-timeline-ruler">
              {ticks(scale, offsetMinutes, locale).map((tick) => (
                <span key={tick.x} className="hatch-timeline-tick" style={{ left: `${tick.x}%` }}>
                  {tick.label}
                </span>
              ))}
            </div>

            <div className="hatch-cumulative-flow-plot-wrap">
              <svg
                ref={svgRef}
                className="hatch-cumulative-flow-plot"
                viewBox={`0 0 ${CHART_UNITS} ${HEIGHT}`}
                preserveAspectRatio="none"
                role="img"
                aria-label={peakLabel(answer.points)}
                tabIndex={0}
                onPointerDown={onPress}
                onPointerMove={onPointerMove}
                onPointerLeave={() => setPointer(null)}
                onPointerUp={stopDragging}
                onPointerCancel={stopDragging}
                onKeyDown={onKeyDown}
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

              {pin !== null && (
                <div
                  className="hatch-cumulative-flow-pin-line"
                  style={{ left: `${xOf(scale, answer.points[pin].at)}%` }}
                  onPointerDown={onPress}
                />
              )}

              {!dragging && pointer && hovered && hoveredBreakdown && (
                <div
                  className="hatch-cumulative-flow-card"
                  aria-hidden
                  style={{ top: pointer.y + GAP, left: near ? pointer.x - GAP - CARD_WIDTH : pointer.x + GAP }}
                >
                  <div className="hatch-cumulative-flow-card-moment">
                    {momentLabel(hovered.at, answer.step, locale)}
                  </div>

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

        <CumulativeFlowPin
          point={pin === null ? null : answer.points[pin]}
          columns={answer.columns}
          statuses={statuses}
          step={answer.step}
          locale={locale}
        />
      </div>
    </div>
  );
}
