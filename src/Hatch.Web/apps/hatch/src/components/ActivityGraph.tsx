import { useEffect, useState } from 'react';
import {
  MAX_WINDOW,
  MEASURES,
  axisWords,
  bars,
  graphLabel,
  linePoints,
  parseWindow,
  peakLabel,
  rollingWords,
  type ActivityMeasure,
} from '../lib/activityGraph';
import type { ActivityReport } from '../types';

/** One bucket's slot, in the SVG's own units. The bar is inset inside it, so
    the gap between bars is arithmetic rather than a stroke. */
const SLOT = 4;
const INSET = 0.4;
const HEIGHT = 100;

/** How long the window field waits after the last keystroke before it writes. */
const SETTLE_MS = 500;

/**
 * The rate of work over time: one bar per bucket and a rolling mean through
 * them.
 *
 * Hand-cut SVG in the house's own tokens, as `SpendGraph` is - no charting
 * dependency. Everything that could be wrong is in `lib/activityGraph.ts` and
 * tested there; what is here places what that returns.
 *
 * **An empty bucket is a zero-height bar, never a gap**, and its full-height
 * slot stays hoverable. **The two measures never share an axis**: the control
 * rescales the whole graph. A measure press changes no read - both measures
 * are in the answer already - so only the window field refetches.
 *
 * **The line is a stroke where every other mark is a fill**, so the SVG's
 * stretch would distort it; it carries `vector-effect: non-scaling-stroke`.
 */
export function ActivityGraph({
  report,
  measure,
  onMeasure,
  windowDays,
  onWindow,
  locale,
}: {
  report: ActivityReport;
  measure: ActivityMeasure;
  onMeasure: (measure: ActivityMeasure) => void;
  /** The window as the page has it - the URL's, or the server's own default. */
  windowDays: number;
  /** Called with a valid window and only with one. */
  onWindow: (days: number) => void;
  /** Pinned by tests; the page passes none and gets the reader's. */
  locale?: string;
}) {
  /* The field's own text, so typing `45` is one refetch and not two: `4` is a
     window too, and is passed over unless the typing stops there. It follows the
     page when the page moves under it - Back, a pasted link - by remembering
     which window it was typed against, rather than copying in an effect. */
  const [typed, setTyped] = useState({ text: String(windowDays), against: windowDays });
  const draft = typed.against === windowDays ? typed.text : String(windowDays);
  const days = parseWindow(draft);

  /* Only a window is ever written. A draft that is not one stays on screen,
     marked, and goes nowhere. */
  useEffect(() => {
    if (days === null || days === windowDays) return;

    const timer = setTimeout(() => onWindow(days), SETTLE_MS);

    return () => clearTimeout(timer);
  }, [days, windowDays, onWindow]);

  const drawn = bars(report, measure, locale);
  const axis = axisWords(report, locale);
  const points = linePoints(drawn, SLOT, HEIGHT);

  /* Enter and leaving the field do not wait for the pause. */
  function commit() {
    if (days === null) setTyped({ text: String(windowDays), against: windowDays });
    else if (days !== windowDays) onWindow(days);
  }

  return (
    <div className="hatch-activity">
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

        {/* The top of the scale, so a shape has a size. */}
        <span className="hatch-activity-peak text-muted">{peakLabel(report, measure)}</span>
      </div>

      <svg
        className="hatch-activity-plot"
        viewBox={`0 0 ${Math.max(1, drawn.length) * SLOT} ${HEIGHT}`}
        preserveAspectRatio="none"
        role="img"
        aria-label={graphLabel(report, measure, locale)}
      >
        {drawn.map((bar, i) => (
          <g key={bar.at}>
            {/* A full-height transparent slot carrying the title, so an empty
                bucket is still hoverable. */}
            <rect className="hatch-activity-slot" x={i * SLOT} y={0} width={SLOT} height={HEIGHT}>
              <title>{bar.label}</title>
            </rect>
            <rect
              className="hatch-activity-bar"
              x={i * SLOT + INSET}
              y={HEIGHT - HEIGHT * bar.fraction}
              width={SLOT - 2 * INSET}
              height={HEIGHT * bar.fraction}
            />
          </g>
        ))}

        {/* A line needs two points; one bucket is a dot. The dot is a zero-length
            round-capped line rather than a <circle>, which the stretch would
            draw as an ellipse - a stroke with a non-scaling width stays round. */}
        {points.length === 1 ? (
          <line
            className="hatch-activity-line hatch-activity-line--dot"
            x1={points[0].x}
            y1={points[0].y}
            x2={points[0].x}
            y2={points[0].y}
          />
        ) : (
          points.length > 1 && (
            <polyline className="hatch-activity-line" points={points.map((p) => `${p.x},${p.y}`).join(' ')} />
          )
        )}
      </svg>

      <div className="hatch-activity-axis text-muted">
        <span>{axis.from}</span>
        <span>{axis.caption}</span>
        <span>{axis.to}</span>
      </div>

      <div className="hatch-activity-rolling">
        <span className="hatch-activity-key" aria-hidden="true" />
        <span className="text-muted">{rollingWords(report.estimate.windowDays, measure, report.bucket)}</span>
        <label className="hatch-activity-window text-muted">
          Window, days
          <input
            type="number"
            inputMode="numeric"
            min={1}
            max={MAX_WINDOW}
            step={1}
            value={draft}
            aria-invalid={days === null}
            onChange={(e) => setTyped({ text: e.target.value, against: windowDays })}
            onKeyDown={(e) => {
              if (e.key === 'Enter') commit();
            }}
            onBlur={commit}
          />
        </label>
      </div>
    </div>
  );
}
