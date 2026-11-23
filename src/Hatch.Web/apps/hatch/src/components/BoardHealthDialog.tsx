import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import type { KeyboardEvent, PointerEvent } from 'react';
import { Link } from 'react-router-dom';
import { Modal } from '@hatch/ui';
import { BARS, axisSentence, ageBandPolygons, bandPolygons, barIndexAt, momentX, plural, stepMoment } from '../lib/boardHealth';
import type { HealthWindow, Tile } from '../lib/boardHealth';
import {
  MARK_PATTERN,
  axisTicks,
  bandCounts,
  clearSummary,
  dialogTitle,
  issueCounts,
  issueRows,
  legend,
  markedWords,
  movedSummary,
  oldestWaitWords,
  rowLabel,
  scaleTop,
  stackedMovedBars,
  waitingSplit,
} from '../lib/boardHealthDialog';
import type { MarkKey, SummaryRow } from '../lib/boardHealthDialog';
import { LANE_LEGEND, allLanes, cellWords, laneStanding, lanesMarkedWords, lastHeardWords } from '../lib/boardHealthLanes';
import type { BoardHealth } from '../types';
import { LaneCell } from './BoardHealthTiles';
import { HealthWindowControl } from './HealthWindowControl';

/* The dialog a health tile opens: the same window as the sparkline, drawn
   large, with an axis, a legend and a sentence for the marked bar. It draws
   from the read's answer alone and holds no state of its own beyond its size,
   so a new answer redraws it in place.

   The chart measures its own svg and draws at that size, as
   CumulativeFlowChart does. It is never stretched with
   `preserveAspectRatio="none"`: that would skew the fill patterns that tell
   the stacked marks apart - stripes change angle, dots turn oval - worst at a
   phone's width. */

/** The size the svg takes before it has been measured, and in static markup. */
const FALLBACK = { width: 480, height: 200 };

const KEYS = ['ArrowLeft', 'ArrowRight', 'Home', 'End'];

/** The classes of the three band marks, which are the tile's own. */
const BAND_CLASS: Partial<Record<MarkKey, string>> = { waiting: 'waiting', inFlight: 'inflight', done: 'done' };

/** One fill pattern: a ground, and the stripes or dots over it. The colours
    come from CSS classes on the mark, never from here. */
function HealthPattern({ id, mark }: { id: string; mark: MarkKey }) {
  const kind = MARK_PATTERN[mark];
  if (!kind) return null;

  return (
    <pattern
      id={id}
      width={6}
      height={6}
      patternUnits="userSpaceOnUse"
      patternTransform={kind === 'stripes' || kind === 'cross' ? 'rotate(45)' : undefined}
    >
      <rect width={6} height={6} className={`hatch-health-dialog-ground hatch-health-dialog-ground--${mark}`} />
      {kind === 'stripes' && <rect width={6} height={2.5} className={`hatch-health-dialog-ink hatch-health-dialog-ink--${mark}`} />}
      {kind === 'cross' && (
        <>
          <rect width={6} height={1.5} className={`hatch-health-dialog-ink hatch-health-dialog-ink--${mark}`} />
          <rect width={1.5} height={6} className={`hatch-health-dialog-ink hatch-health-dialog-ink--${mark}`} />
        </>
      )}
      {kind === 'dots' && <circle cx={3} cy={3} r={1.4} className={`hatch-health-dialog-ink hatch-health-dialog-ink--${mark}`} />}
    </pattern>
  );
}

/** A legend swatch drawn the way its mark is: the same pattern or band. */
function Swatch({ base, mark }: { base: string; mark: MarkKey }) {
  const id = `${base}-swatch-${mark}`;
  const band = BAND_CLASS[mark];

  return (
    <svg className="hatch-health-dialog-swatch" width={22} height={14} viewBox="0 0 22 14" aria-hidden="true" focusable="false">
      {MARK_PATTERN[mark] && (
        <>
          <defs>
            <HealthPattern id={id} mark={mark} />
          </defs>
          <rect className={`hatch-health-dialog-seg hatch-health-dialog-seg--${mark}`} x={0.5} y={0.5} width={21} height={13} fill={`url(#${id})`} />
        </>
      )}
      {band && <rect className={`hatch-health-band hatch-health-band--${band}`} x={0} y={0} width={22} height={14} />}
    </svg>
  );
}

/** The Oldest in flight chart's own marks: the tile's four age bands, drawn
    large and told apart by pattern as well as shade. Past normal is on the
    baseline, aging above it, healthy above that, and a grey band on top only
    when an issue stands in a column with no normal yet. Their own classes, not
    the tile's `hatch-health-band` ones: those set a CSS fill that would beat the
    pattern's fill attribute. Its own component so the list of issues that comes
    after extends it without touching the chart around it. */
export function OldestChart({
  answer,
  width,
  height,
  pattern,
}: {
  answer: BoardHealth;
  width: number;
  height: number;
  pattern: (mark: MarkKey) => string;
}) {
  const bands = ageBandPolygons(answer, width, height);
  const drawn: [MarkKey, string][] = [
    ['healthy', bands.healthy],
    ['aging', bands.aging],
    ['pastNormal', bands.pastNormal],
  ];
  if (bands.noNormal) drawn.push(['noNormal', bands.noNormal]);

  return (
    <>
      {drawn.map(([mark, points]) => (
        <polygon
          key={mark}
          className={`hatch-health-dialog-seg hatch-health-dialog-seg--${mark}`}
          fill={`url(#${pattern(mark)})`}
          points={points}
        />
      ))}
    </>
  );
}

/** The time labels along the bottom of a chart, placed by percentage of the width they sit in. */
function TimeAxis({ answer }: { answer: BoardHealth }) {
  return (
    <div className="hatch-health-dialog-ticks" aria-hidden="true">
      {axisTicks(answer, 100).map((tick) => (
        <span
          key={tick.x}
          className={`hatch-health-dialog-tick${tick.x === 0 ? ' hatch-health-dialog-tick--start' : ''}${tick.x === 100 ? ' hatch-health-dialog-tick--end' : ''}`}
          style={{ left: `${tick.x}%` }}
        >
          {tick.label}
        </span>
      ))}
    </div>
  );
}

/** The Runners dialog's chart: a row of 24 cells for every lane the read answers,
    none left out. The box scrolls when there are many. The time axis is the
    box's last child and sticks to its bottom, so it stays in view and, sharing
    the box with the cells, cannot drift from them when a scrollbar takes some of
    the width. The box is the focusable group the other dialogs' charts are, so
    Up and Down scroll it as the browser does and only the four mark keys are
    taken. */
function LanesChart({
  answer,
  index,
  onIndex,
  label,
}: {
  answer: BoardHealth;
  index: number | null;
  onIndex: (index: number | null) => void;
  label: string;
}) {
  const lanes = allLanes(answer);

  /* Every row's cells share one x-range, so the first row's stands for all. Off
     the cells - over the names, say - there is no bar, and the mark clears. */
  const onPointer = (event: PointerEvent<HTMLDivElement>) => {
    const cells = event.currentTarget.querySelector('.hatch-health-dialog-lane-cells');
    if (!cells) {
      onIndex(null);
      return;
    }
    const rect = cells.getBoundingClientRect();
    onIndex(barIndexAt(event.clientX - rect.left, rect.width));
  };

  /* Escape is left to Modal, which closes the dialog on it. */
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!KEYS.includes(event.key)) return;
    event.preventDefault();
    onIndex(stepMoment(index, event.key, answer.bars.length));
  };

  return (
    <div
      className="hatch-health-dialog-lanes"
      data-part="chart"
      tabIndex={0}
      role="group"
      aria-label={label}
      onPointerMove={onPointer}
      onPointerDown={onPointer}
      onPointerLeave={() => onIndex(null)}
      onKeyDown={onKeyDown}
    >
      {lanes.length === 0 ? (
        <p className="hatch-health-dialog-empty">No runner was up in this window</p>
      ) : (
        <>
          <div className="hatch-health-dialog-lane-rows">
            {lanes.map((lane) => (
              <div key={lane.name} className="hatch-health-dialog-lane" data-lane={lane.name}>
                <div className="hatch-health-dialog-lane-name">
                  <strong title={lane.name}>{lane.name}</strong>
                  <span data-part="standing">{laneStanding(lane)}</span>
                  <span data-part="heard">{lastHeardWords(lane, answer)}</span>
                </div>
                <div className="hatch-health-dialog-lane-cells">
                  {lane.cells.map((cell, i) => (
                    <LaneCell key={i} cell={cell} title={`${lane.name}: ${cellWords(cell)}`} />
                  ))}
                </div>
              </div>
            ))}
            {index !== null && (
              <span
                className="hatch-health-dialog-lane-moment"
                style={{
                  left: `calc(var(--hatch-lane-name) + var(--sp-2) + (100% - var(--hatch-lane-name) - var(--sp-2)) * (${index} + 0.5) / ${BARS})`,
                }}
              />
            )}
          </div>
          <div className="hatch-health-dialog-lane-axis">
            <TimeAxis answer={answer} />
          </div>
        </>
      )}
    </div>
  );
}

/** The seven lane states, each a swatch drawn as the cell itself, then its words. */
function LaneLegend() {
  return (
    <ul className="hatch-health-dialog-legend" aria-label="Legend">
      {LANE_LEGEND.map((line) => (
        <li key={line.title} data-state={line.state}>
          <span className="hatch-health-dialog-lane-swatch" aria-hidden="true">
            <LaneCell cell={{ state: line.state, why: null }} />
          </span>
          <span>
            <strong>{line.title}</strong>: {line.body}
          </span>
        </li>
      ))}
    </ul>
  );
}

/** The Oldest dialog's list of issues in flight, longest stay first. A two-way control narrows it to the issues past
    normal; each side shows its count, both read from the issues themselves, so the past normal side is the tile's
    own count. A row is one link to its issue and draws its time as a bar on the list's one shared scale, with a mark
    where its column's normal falls. A row past normal carries the warning sign and the words; the other bands have
    words too, so no row is told by colour alone. The list scrolls inside the dialog. */
function IssueList({ answer, pastOnly: seed }: { answer: BoardHealth; pastOnly: boolean }) {
  const [pastOnly, setPastOnly] = useState(seed);
  const block = useRef<HTMLElement>(null);
  const counts = issueCounts(answer);
  const rows = issueRows(answer, pastOnly);
  const sides: [boolean, string, number][] = [
    [false, 'All', counts.all],
    [true, 'Past normal', counts.pastNormal],
  ];

  /* Opened from the warning, the list is what was asked for: bring it into view, as a phone's tall chart would
     otherwise leave it below the fold. Once, on mount - the dialog is mounted fresh on each open. */
  useEffect(() => {
    if (seed) block.current?.scrollIntoView?.({ block: 'nearest' });
  }, [seed]);

  return (
    <section ref={block} className="hatch-health-dialog-issues-block" aria-label="Issues in flight">
      <div className="hatch-health-issue-sides" role="group" aria-label="Show" data-part="issue-filter">
        {sides.map(([past, label, count]) => (
          <button
            key={label}
            type="button"
            className={`hatch-health-issue-side${past === pastOnly ? ' active' : ''}`}
            aria-pressed={past === pastOnly}
            onClick={() => setPastOnly(past)}
          >
            {label} {count}
          </button>
        ))}
      </div>
      {rows.length === 0 ? (
        <p className="hatch-health-dialog-note" data-part="issues-empty">
          {counts.all === 0 ? 'Nothing is in flight' : 'No issue is past normal'}
        </p>
      ) : (
        <ul className="hatch-health-dialog-issues" data-part="issues">
          {rows.map((row) => (
            <li key={row.key}>
              <Link to={row.href} className={`hatch-health-dialog-issue hatch-health-dialog-issue--${row.band}`} aria-label={rowLabel(row)} data-issue={row.key}>
                <span className="hatch-health-dialog-issue-name">
                  <strong>{row.key}</strong> {row.title}
                </span>
                <span className="hatch-health-dialog-issue-stood">
                  {row.statusName} · {row.stood}
                </span>
                {row.fillPercent !== null && row.normalPercent !== null && (
                  <span className="hatch-health-dialog-issue-track" aria-hidden="true">
                    <span className="hatch-health-dialog-issue-fill" style={{ width: `${row.fillPercent}%` }} />
                    <span className="hatch-health-dialog-issue-normal" style={{ left: `${row.normalPercent}%` }} />
                  </span>
                )}
                <span className="hatch-health-dialog-issue-band">
                  {row.sign && (
                    <span className="hatch-health-glyph" aria-hidden="true">
                      ⚠
                    </span>
                  )}{' '}
                  {row.bandWords}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function Rows({ rows }: { rows: SummaryRow[] }) {
  return (
    <dl className="hatch-health-dialog-rows">
      {rows.map((row) => (
        <div key={row.key} className="hatch-health-dialog-row" data-row={row.key}>
          <dt>{row.label}</dt>
          <dd>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function BoardHealthDialog({
  tile,
  answer,
  index,
  onIndex,
  window: chosen,
  onWindow,
  onClose,
  href,
  pastOnly = false,
}: {
  tile: Tile;
  answer: BoardHealth;
  /** The marked bar, or null for the window at rest. */
  index: number | null;
  onIndex: (index: number | null) => void;
  window: HealthWindow;
  onWindow: (choice: HealthWindow) => void;
  onClose: () => void;
  /** The cumulative flow report on the same window and projects, for In flight. */
  href?: string;
  /** Opens the Oldest list narrowed to the issues past normal. It only seeds the list: the dialog is mounted fresh
      on each open. */
  pastOnly?: boolean;
}) {
  const base = `hd-${useId().replace(/:/g, '')}`;
  const svgRef = useRef<SVGSVGElement>(null);
  const [size, setSize] = useState(FALLBACK);
  const title = dialogTitle(tile, answer);
  const marks = legend(tile, answer);
  const { width, height } = size;
  const patternOf = (mark: MarkKey): string => `${base}-${mark}`;

  useLayoutEffect(() => {
    const svg = svgRef.current;
    if (!svg) return;

    const measure = () => {
      const rect = svg.getBoundingClientRect();
      if (!(rect.width > 0) || !(rect.height > 0)) return;
      setSize((current) =>
        current.width === rect.width && current.height === rect.height ? current : { width: rect.width, height: rect.height },
      );
    };

    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(svg);

    return () => observer.disconnect();
  }, []);

  const onPointer = (event: PointerEvent<HTMLDivElement>) => {
    const rect = (svgRef.current ?? event.currentTarget).getBoundingClientRect();
    onIndex(barIndexAt(event.clientX - rect.left, rect.width));
  };

  /* Escape is left to Modal, which closes the dialog on it. */
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!KEYS.includes(event.key)) return;
    event.preventDefault();
    onIndex(stepMoment(index, event.key, answer.bars.length));
  };

  return (
    <Modal open onClose={onClose} title={title}>
      <div className="hatch-health-dialog" data-dialog={tile}>
        <div className="hatch-health-dialog-control">
          <HealthWindowControl window={chosen} onWindow={onWindow} />
          <p className="hatch-health-axis">{axisSentence(answer)}</p>
        </div>

        {tile === 'runners' ? (
          <LanesChart answer={answer} index={index} onIndex={onIndex} label={`${title}. Arrow keys mark a moment.`} />
        ) : (
        <div className="hatch-health-dialog-plot">
          <div className="hatch-health-dialog-scale" aria-hidden="true">
            <span data-part="scale-top">{scaleTop(tile, answer)}</span>
            <span>0</span>
          </div>

          <div
            className="hatch-health-dialog-chart"
            data-part="chart"
            tabIndex={0}
            role="group"
            aria-label={`${title}. Arrow keys mark a moment.`}
            onPointerMove={onPointer}
            onPointerDown={onPointer}
            onPointerLeave={() => onIndex(null)}
            onKeyDown={onKeyDown}
          >
            <svg
              ref={svgRef}
              className="hatch-health-dialog-svg"
              viewBox={`0 0 ${width} ${height}`}
              aria-hidden="true"
              focusable="false"
            >
              <defs>
                {marks.map((mark) => (
                  <HealthPattern key={mark.key} id={patternOf(mark.key)} mark={mark.key} />
                ))}
              </defs>
              <line className="hatch-health-dialog-floor" x1={0} x2={width} y1={height} y2={height} />

              {tile === 'moved' &&
                stackedMovedBars(answer, width, height).map((bar) => (
                  <g key={bar.index} className="hatch-health-dialog-bar" data-bar={bar.index}>
                    {bar.segments.map((segment) => (
                      <rect
                        key={segment.source}
                        className={`hatch-health-dialog-seg hatch-health-dialog-seg--${segment.source}`}
                        fill={`url(#${patternOf(segment.source)})`}
                        x={bar.x}
                        y={segment.y}
                        width={bar.width}
                        height={segment.height}
                      />
                    ))}
                  </g>
                ))}

              {tile === 'inFlight' && <InFlightMarks answer={answer} width={width} height={height} />}

              {tile === 'oldest' && <OldestChart answer={answer} width={width} height={height} pattern={patternOf} />}

              {tile === 'waiting' && <WaitingMarks answer={answer} width={width} height={height} pattern={patternOf} />}

              {index !== null && (
                <line
                  className="hatch-health-dialog-moment"
                  x1={momentX(index, width)}
                  x2={momentX(index, width)}
                  y1={0}
                  y2={height}
                />
              )}
            </svg>
          </div>

          <TimeAxis answer={answer} />
        </div>
        )}

        {tile === 'runners' ? (
          <LaneLegend />
        ) : (
          <ul className="hatch-health-dialog-legend" aria-label="Legend">
            {marks.map((mark) => (
              <li key={mark.key} data-mark={mark.key}>
                <Swatch base={base} mark={mark.key} />
                {mark.label}
              </li>
            ))}
          </ul>
        )}

        <p className="hatch-health-dialog-words" data-part="words" aria-live="polite">
          {tile === 'runners' ? lanesMarkedWords(answer, index) : markedWords(tile, answer, index)}
        </p>

        {tile === 'runners' && (
          <p className="hatch-health-dialog-note">{plural(allLanes(answer).length, 'lane', 'lanes')}</p>
        )}

        {tile === 'moved' && <Rows rows={movedSummary(answer)} />}

        {tile === 'inFlight' && (
          <>
            <Rows rows={bandCounts(answer, index)} />
            {href && (
              <Link to={href} className="hatch-health-dialog-link">
                Open the cumulative flow report
              </Link>
            )}
          </>
        )}

        {tile === 'oldest' && <IssueList answer={answer} pastOnly={pastOnly} />}

        {tile === 'waiting' &&
          (clearSummary(answer).length > 0 ? (
            <Rows rows={clearSummary(answer)} />
          ) : (
            <p className="hatch-health-dialog-note">Now: {oldestWaitWords(answer)}</p>
          ))}
      </div>
    </Modal>
  );
}

function InFlightMarks({ answer, width, height }: { answer: BoardHealth; width: number; height: number }) {
  const bands = bandPolygons(answer, width, height);

  return (
    <>
      <polygon className="hatch-health-band hatch-health-band--done" points={bands.done} />
      <polygon className="hatch-health-band hatch-health-band--inflight" points={bands.inFlight} />
      <polygon className="hatch-health-band hatch-health-band--waiting" points={bands.waiting} />
    </>
  );
}

function WaitingMarks({
  answer,
  width,
  height,
  pattern,
}: {
  answer: BoardHealth;
  width: number;
  height: number;
  pattern: (mark: MarkKey) => string;
}) {
  const split = waitingSplit(answer, width, height);

  return (
    <>
      <path
        className="hatch-health-dialog-seg hatch-health-dialog-seg--pullRequests"
        fill={`url(#${pattern('pullRequests')})`}
        d={split.pullRequests.area}
      />
      <path
        className="hatch-health-dialog-seg hatch-health-dialog-seg--questions"
        fill={`url(#${pattern('questions')})`}
        d={split.questions.area}
      />
      <path className="hatch-health-dialog-edge" d={split.pullRequests.line} fill="none" />
      <path className="hatch-health-dialog-edge" d={split.questions.line} fill="none" />
    </>
  );
}
