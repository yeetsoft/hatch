import { useLayoutEffect, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from 'react';
import {
  bandEdge,
  bandPolygon,
  bandStyle,
  bands,
  breakdown,
  cutKeys,
  drawOrder,
  labelFor,
  momentLabel,
  peakLabel,
  pinAfter,
  pointIndexAt,
  slots,
} from '../lib/cumulativeFlow';
import type { ColourBy, PinAction } from '../lib/cumulativeFlow';
import { CHART_UNITS, ticks, todayX, xOf, type Scale } from '../lib/timeline';
import { CumulativeFlowPin } from './CumulativeFlowPin';
import type { CumulativeFlow, Project, Status } from '../types';

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
  by,
  projects,
  scale,
  offsetMinutes,
  now,
  locale,
}: {
  answer: CumulativeFlow;
  statuses: Status[];
  by: ColourBy;
  projects: Project[];
  scale: Scale;
  offsetMinutes: number;
  now: Date;
  locale?: string;
}) {
  const keys = cutKeys(answer, by);
  const series = bands(answer, scale, by);
  const today = todayX(scale, now);
  const slotted = slots(answer.points, scale);
  const svgRef = useRef<SVGSVGElement>(null);
  const [pointer, setPointer] = useState<{ index: number; x: number; y: number } | null>(null);
  const [pin, setPin] = useState<number | null>(null);
  const [dragging, setDragging] = useState(false);

  /* The first-render and server-render fallback - `renderToStaticMarkup`
     runs no effects, so this is the size the page tests see. The
     `ResizeObserver` below replaces it with the svg's own measured box the
     moment it mounts in a real DOM. */
  const [size, setSize] = useState({ width: 600, height: 200 });

  useLayoutEffect(() => {
    const svg = svgRef.current;
    if (!svg) return;

    const measure = () => {
      const rect = svg.getBoundingClientRect();
      setSize((current) =>
        current.width === rect.width && current.height === rect.height
          ? current
          : { width: rect.width, height: rect.height },
      );
    };

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(svg);

    return () => observer.disconnect();
  }, []);

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
  const hoveredBreakdown = hovered ? breakdown(hovered, keys, by, statuses) : null;
  const near =
    pointer !== null &&
    svgRef.current !== null &&
    pointer.x >= svgRef.current.getBoundingClientRect().right - CARD_WIDTH - GAP;

  return (
    <div className="hatch-cumulative-flow">
      <div className="hatch-cumulative-flow-legend">
        {keys.map((key) => (
          <span key={key} className="hatch-cumulative-flow-legend-item">
            <span className="hatch-cumulative-flow-swatch" style={bandStyle(by, key, statuses, projects)} />
            {labelFor(by, key, statuses)}
          </span>
        ))}

        {/* Pushed to the far end, as the spend graph's own peak label is -
            the rule that does it is layout-only and not spend-specific. */}
        <span className="hatch-spend-peak text-muted">{peakLabel(answer.points, by)}</span>
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
                viewBox={`0 0 ${size.width} ${size.height}`}
                role="img"
                aria-label={peakLabel(answer.points, by)}
                tabIndex={0}
                onPointerDown={onPress}
                onPointerMove={onPointerMove}
                onPointerLeave={() => setPointer(null)}
                onPointerUp={stopDragging}
                onPointerCancel={stopDragging}
                onKeyDown={onKeyDown}
              >
                {drawOrder(keys, series).map(({ key, band }) => (
                  <polygon
                    key={key}
                    className="hatch-timeline-segment"
                    points={bandPolygon(band, size.width, size.height)}
                    style={bandStyle(by, key, statuses, projects)}
                  />
                ))}

                {drawOrder(keys, series).map(({ key, band }) => (
                  <polyline
                    key={key}
                    className="hatch-cumulative-flow-edge"
                    points={bandEdge(band, size.width, size.height)}
                  />
                ))}

                {answer.points.map((point, i) => (
                  <rect
                    key={point.at}
                    className="hatch-spend-slot"
                    x={(slotted[i].x * size.width) / CHART_UNITS}
                    y={0}
                    width={(slotted[i].width * size.width) / CHART_UNITS}
                    height={size.height}
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
                    {momentLabel(hovered.at, answer.stepMinutes, locale)}
                  </div>

                  {hoveredBreakdown.rows.map((row) => (
                    <div key={row.key} className="hatch-cumulative-flow-card-row">
                      <span className="hatch-cumulative-flow-swatch" style={bandStyle(by, row.key, statuses, projects)} />
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
          keys={keys}
          by={by}
          statuses={statuses}
          projects={projects}
          stepMinutes={answer.stepMinutes}
          locale={locale}
        />
      </div>
    </div>
  );
}
