import { useId, useState } from 'react';
import type { CSSProperties, KeyboardEvent, PointerEvent, ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  BARS,
  bandPolygons,
  barIndexAt,
  momentX,
  movedBars,
  oldestLine,
  stepMoment,
  tileLabel,
  waitingSteps,
} from '../lib/boardHealth';
import type { Tile, TileReadout } from '../lib/boardHealth';
import { LANE_LEGEND, cellWords, drawnLanes, hoverList } from '../lib/boardHealthLanes';
import type { BoardHealth, BoardHealthCell } from '../types';

/* The four tiles of the board health strip. Each draws from the read's answer
   alone: nothing here knows the board page, the router's location or the scope
   (only the In flight tile's `href`, handed in), so another page could draw the
   same tile. Every number and every point of geometry comes from
   lib/boardHealth; this file only draws. */

/** The sparkline's own coordinate space. It is stretched to the tile's width
    with `preserveAspectRatio="none"`, so strokes use `vectorEffect` to stay the
    width they are given and a dot is a zero-length round-capped stroke, not a
    circle that would stretch into an oval. */
const WIDTH = 240;
const HEIGHT = 40;

interface TileProps {
  answer: BoardHealth;
  /** The marked bar, or null for the window at rest. */
  index: number | null;
  onIndex: (index: number | null) => void;
  readout: TileReadout;
}

/** The glyph said in a word, so it is never the whole of a signal. */
const GLYPH_WORD: Record<string, string> = { '▲': 'up', '▼': 'down', '–': 'unchanged', '⚠': 'warning' };

const KEYS = ['ArrowLeft', 'ArrowRight', 'Home', 'End', 'Escape'];

function HealthTile({
  tile,
  label,
  readout,
  answer,
  index,
  onIndex,
  href,
  onActivate,
  children,
  body,
  keyButton,
  legend,
  popover,
  hover,
}: TileProps & {
  tile: Tile;
  label: string;
  href?: string;
  /** What a click on the sparkline does, for a tile that leads somewhere. */
  onActivate?: () => void;
  /** The sparkline's marks, drawn in the box `WIDTH` by `HEIGHT`. Ignored when `body` is given. */
  children?: ReactNode;
  /** Replaces the svg sparkline outright - the Runners tile's HTML lanes, which draw their own moment
      marker rather than the shared svg one. */
  body?: ReactNode;
  /** A control beside the label, absolutely placed at the tile's top right - the Runners tile's Key. */
  keyButton?: ReactNode;
  /** Always in the markup, shown by CSS - the Runners tile's legend. */
  legend?: ReactNode;
  /** The hover popover under the tile, drawn only while a bar is marked. */
  popover?: ReactNode;
  /** The hover list, said last in the tile's spoken label. */
  hover?: string;
}) {
  const count = answer.bars.length;

  const onMove = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onIndex(barIndexAt(event.clientX - rect.left, rect.width));
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (!KEYS.includes(event.key)) return;
    event.preventDefault();
    onIndex(stepMoment(index, event.key, count));
  };

  return (
    <section
      className={`hatch-health-tile hatch-health-tile--${readout.tone}`}
      aria-label={tileLabel(tile, readout, hover)}
      data-tile={tile}
    >
      <h3 className="hatch-health-label">
        {href ? (
          <Link to={href} className="hatch-health-link">
            {label}
          </Link>
        ) : (
          label
        )}
      </h3>
      {keyButton}
      <p className="hatch-health-value">
        <span className="hatch-health-number" data-part="value">
          {readout.value}
        </span>
        {readout.glyph && (
          <span className="hatch-health-glyph" role="img" aria-label={GLYPH_WORD[readout.glyph] ?? ''}>
            {readout.glyph}
          </span>
        )}
      </p>
      <p className="hatch-health-second" data-part="second" title={readout.second}>
        {readout.second}
      </p>
      <p className="hatch-health-detail" data-part="detail" title={readout.detail || undefined}>
        {readout.detail}
      </p>
      <div
        className={`hatch-health-spark${onActivate ? ' hatch-health-spark--link' : ''}`}
        data-part="spark"
        tabIndex={0}
        role="group"
        aria-label={`${label} over the window. Arrow keys mark a moment.`}
        onPointerMove={onMove}
        onPointerLeave={() => onIndex(null)}
        onKeyDown={onKeyDown}
        onBlur={() => onIndex(null)}
        onClick={onActivate}
      >
        {body ?? (
          <svg
            viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
            preserveAspectRatio="none"
            className="hatch-health-svg"
            aria-hidden="true"
            focusable="false"
          >
            {children}
            {index !== null && (
              <line
                className="hatch-health-moment"
                x1={momentX(index, WIDTH)}
                x2={momentX(index, WIDTH)}
                y1={0}
                y2={HEIGHT}
                vectorEffect="non-scaling-stroke"
              />
            )}
          </svg>
        )}
      </div>
      {popover}
      {legend}
    </section>
  );
}

/** A dot at `x, y`: a zero-length stroke with a round cap. */
const dot = (x: number, y: number): string => `M ${x} ${y} h 0`;

export function MovedTile({ answer, index, onIndex, readout }: TileProps) {
  const latest = answer.bars.length - 1;

  return (
    <HealthTile tile="moved" label="Moved" readout={readout} answer={answer} index={index} onIndex={onIndex}>
      {movedBars(answer, WIDTH, HEIGHT).map((rect) => (
        <rect
          key={rect.index}
          className={`hatch-health-bar${rect.index === latest ? ' hatch-health-bar--latest' : ''}`}
          x={rect.x}
          y={rect.y}
          width={rect.width}
          height={rect.height}
        />
      ))}
    </HealthTile>
  );
}

export function InFlightTile({ answer, index, onIndex, readout, href }: TileProps & { href: string }) {
  const navigate = useNavigate();
  const bands = bandPolygons(answer, WIDTH, HEIGHT);

  return (
    <HealthTile
      tile="inFlight"
      label="In flight"
      readout={readout}
      answer={answer}
      index={index}
      onIndex={onIndex}
      href={href}
      onActivate={() => navigate(href)}
    >
      <polygon className="hatch-health-band hatch-health-band--done" points={bands.done} />
      <polygon className="hatch-health-band hatch-health-band--inflight" points={bands.inFlight} />
      <polygon className="hatch-health-band hatch-health-band--waiting" points={bands.waiting} />
    </HealthTile>
  );
}

export function OldestTile({ answer, index, onIndex, readout }: TileProps) {
  const line = oldestLine(answer, WIDTH, HEIGHT);

  return (
    <HealthTile tile="oldest" label="Oldest in flight" readout={readout} answer={answer} index={index} onIndex={onIndex}>
      <path className="hatch-health-line" d={line.path} vectorEffect="non-scaling-stroke" fill="none" />
      {line.last && <path className="hatch-health-dot" d={dot(line.last.x, line.last.y)} vectorEffect="non-scaling-stroke" fill="none" />}
    </HealthTile>
  );
}

export function WaitingTile({ answer, index, onIndex, readout }: TileProps) {
  const steps = waitingSteps(answer, WIDTH, HEIGHT);

  return (
    <HealthTile tile="waiting" label="Waiting on you" readout={readout} answer={answer} index={index} onIndex={onIndex}>
      <path className="hatch-health-area" d={steps.area} />
      <path className="hatch-health-line" d={steps.line} vectorEffect="non-scaling-stroke" fill="none" />
      {steps.last && <path className="hatch-health-dot" d={dot(steps.last.x, steps.last.y)} vectorEffect="non-scaling-stroke" fill="none" />}
    </HealthTile>
  );
}

/** A cell's class, from its state and - for a board pause only, which draws
    as paused rather than striped - its why. */
function cellClass(cell: BoardHealthCell): string {
  const base = `hatch-health-lane-cell hatch-health-lane-cell--${cell.state}`;
  return cell.why === 'board' ? `${base} hatch-health-lane-cell--board` : base;
}

/**
 * The Runners tile: a thin lane per runner, at most eight, with a hover list
 * under it and a legend one press away. Its body replaces the svg sparkline
 * with 24 HTML cells a row - the svg would stretch hollow and striped cells
 * into a skew - so its moment marker is its own, `hatch-health-lane-moment`,
 * not the shared `hatch-health-moment` the other four tiles count on.
 */
export function RunnersTile({ answer, index, onIndex, readout }: TileProps) {
  const { lanes } = drawnLanes(answer);
  const hover = index !== null ? hoverList(answer, index) : null;
  const legendId = useId();
  const [legendOpen, setLegendOpen] = useState(false);

  const hoverSentence = hover
    ? [...hover.lines.map((line) => `${line.name} ${line.words}`), hover.omitted > 0 ? `${hover.omitted} not drawn` : null]
        .filter((part): part is string => Boolean(part))
        .join(', ')
    : undefined;

  const keyButton = (
    <button
      type="button"
      className="hatch-health-key"
      aria-expanded={legendOpen}
      aria-controls={legendId}
      title="Key"
      onClick={() => setLegendOpen((was) => !was)}
      onKeyDown={(event) => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        setLegendOpen(false);
      }}
      onBlur={() => setLegendOpen(false)}
    >
      Key
    </button>
  );

  const legend = (
    <div id={legendId} className={`hatch-health-legend${legendOpen ? ' open' : ''}`}>
      {LANE_LEGEND.map((line) => (
        <p key={line.title}>
          <strong>{line.title}</strong>: {line.body}
        </p>
      ))}
    </div>
  );

  const popover = hover && (
    <div className="hatch-health-lane-hover" aria-hidden="true">
      {hover.lines.map((line) => (
        <p key={line.name}>
          <strong>{line.name}</strong> {line.words}
        </p>
      ))}
      {hover.omitted > 0 && <p>{hover.omitted} not drawn</p>}
    </div>
  );

  const body = (
    <div className="hatch-health-lanes" style={{ '--hatch-lanes': lanes.length } as CSSProperties}>
      <div className="hatch-health-lane-names">
        {lanes.map((lane) => (
          <span key={lane.name} className="hatch-health-lane-name" title={lane.name}>
            {lane.name}
          </span>
        ))}
      </div>
      <div className="hatch-health-lane-grid">
        {lanes.map((lane) => (
          <div className="hatch-health-lane-cells" key={lane.name}>
            {lane.cells.map((cell, i) => (
              <span key={i} className={cellClass(cell)} title={cellWords(cell)} />
            ))}
          </div>
        ))}
        {index !== null && (
          <span
            className="hatch-health-lane-moment"
            style={{ left: `calc(100% * (${index} + 0.5) / ${BARS})` }}
          />
        )}
      </div>
    </div>
  );

  return (
    <HealthTile
      tile="runners"
      label="Runners"
      readout={readout}
      answer={answer}
      index={index}
      onIndex={onIndex}
      body={body}
      keyButton={keyButton}
      legend={legend}
      popover={popover}
      hover={hoverSentence}
    />
  );
}
