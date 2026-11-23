import { useId, useRef, useState } from 'react';
import type { CSSProperties, KeyboardEvent, MouseEvent, PointerEvent, ReactNode } from 'react';
import { Link } from 'react-router-dom';
import {
  BARS,
  ageBandPolygons,
  bandPolygons,
  barIndexAt,
  issuePeek,
  momentX,
  movedBars,
  oldestIssue,
  stepMoment,
  tileLabel,
  waitingSteps,
  warningLead,
} from '../lib/boardHealth';
import type { IssuePeek, Tile, TileReadout } from '../lib/boardHealth';
import { LANE_LEGEND, cellWords, drawnLanes, hoverList } from '../lib/boardHealthLanes';
import type { BoardHealth, BoardHealthCell } from '../types';

/* The five tiles of the board health strip. Each draws from the read's answer
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
  /** Opens this tile's dialog. Left off, the tile only marks a moment. */
  onOpen?: () => void;
}

/** The glyph said in a word, so it is never the whole of a signal. */
const GLYPH_WORD: Record<string, string> = { '▲': 'up', '▼': 'down', '–': 'unchanged', '⚠': 'warning' };

/** Where a tile's warning leads: a link to an issue, or a press that opens something. Its `label` is the spoken
    name of both controls the warning is drawn as - the sign and the line beside it. */
export type TileLead = { to: string; label: string } | { onPress: () => void; label: string };

const KEYS = ['ArrowLeft', 'ArrowRight', 'Home', 'End', 'Escape'];

/** The warning sign, as the tile's readout carries it. */
const WARNING = '⚠';

/** The warning's two controls - the sign and the line - are one component, so they lead the same way and say the
    same name. With no lead, the children are drawn plain, as text. A button focuses itself first: WebKit does not
    focus a pressed button, and the dialog records the focused element as its opener to hand focus back to it. */
function LeadControl({ lead, className, children }: { lead?: TileLead; className: string; children: ReactNode }) {
  if (!lead) return <>{children}</>;

  const own = `hatch-health-lead ${className}`;

  return 'to' in lead ? (
    <Link to={lead.to} className={own} aria-label={lead.label}>
      {children}
    </Link>
  ) : (
    <button
      type="button"
      className={own}
      aria-label={lead.label}
      onClick={(event) => {
        event.currentTarget.focus();
        lead.onPress();
      }}
    >
      {children}
    </button>
  );
}

/** What a press on a tile leaves to the thing pressed: a link, a button, or anything a tile marks as its own
    control with `data-tile-control`. */
const OWN_CONTROL = 'a, button, [data-tile-control]';

function HealthTile({
  tile,
  label,
  readout,
  answer,
  index,
  onIndex,
  href,
  onOpen,
  children,
  body,
  keyButton,
  legend,
  popover,
  hover,
  detailHref,
  peek,
  lead,
}: TileProps & {
  tile: Tile;
  label: string;
  href?: string;
  /** Opens the tile's dialog. A press on the tile anywhere but a control of its own, or Enter or Space on the
      focused sparkline, calls it. A tile without one only marks. */
  onOpen?: () => void;
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
  /** Makes the detail line a link to this path - set only while no bar is marked. */
  detailHref?: string;
  /** A note shown on pointing at, or focusing, the number or the detail line. */
  peek?: IssuePeek;
  /** Where the warning sign and the line beside it lead. A warning is never drawn bare: with `⚠` in the readout and
      no lead, nothing is drawn for it. */
  lead?: TileLead;
}) {
  const count = answer.bars.length;
  const peekId = useId();
  const sparkRef = useRef<HTMLDivElement>(null);

  const onMove = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    onIndex(barIndexAt(event.clientX - rect.left, rect.width));
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (onOpen && (event.key === 'Enter' || event.key === ' ')) {
      event.preventDefault();
      onOpen();
      return;
    }
    if (!KEYS.includes(event.key)) return;
    event.preventDefault();
    onIndex(stepMoment(index, event.key, count));
  };

  /* Focus goes to the sparkline first, so the dialog's Modal records it as the opener and hands focus back to
     it on close - a press on a blank part of the tile has focused nothing of its own. */
  const onPress = (event: MouseEvent<HTMLElement>) => {
    if (!onOpen) return;
    if (event.target instanceof Element && event.target.closest(OWN_CONTROL)) return;
    sparkRef.current?.focus();
    onOpen();
  };

  return (
    <section
      className={`hatch-health-tile hatch-health-tile--${readout.tone}`}
      aria-label={tileLabel(tile, readout, hover)}
      data-tile={tile}
      onClick={onPress}
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
      <p
        className="hatch-health-value"
        tabIndex={peek ? 0 : undefined}
        aria-describedby={peek ? peekId : undefined}
      >
        <span className="hatch-health-number" data-part="value">
          {readout.value}
        </span>
        {readout.glyph && (readout.glyph !== WARNING || lead) && (
          <LeadControl lead={readout.glyph === WARNING ? lead : undefined} className="hatch-health-glyph-lead">
            <span className="hatch-health-glyph" role="img" aria-label={GLYPH_WORD[readout.glyph] ?? ''}>
              {readout.glyph}
            </span>
          </LeadControl>
        )}
      </p>
      <p className="hatch-health-second" data-part="second" title={readout.second}>
        <LeadControl lead={readout.glyph === WARNING ? lead : undefined} className="hatch-health-second-lead">
          {readout.second}
        </LeadControl>
      </p>
      <p className="hatch-health-detail" data-part="detail" title={readout.detail || undefined}>
        {detailHref && readout.detail ? (
          <Link to={detailHref} className="hatch-health-detail-link">
            {readout.detail}
          </Link>
        ) : (
          readout.detail
        )}
      </p>
      {peek && (
        <div id={peekId} className="hatch-health-peek" role="tooltip">
          <p className="hatch-health-peek-title">{peek.heading}</p>
          <p>
            {peek.stood} · {peek.normal}
          </p>
        </div>
      )}
      <div
        ref={sparkRef}
        className={`hatch-health-spark${onOpen ? ' hatch-health-spark--open' : ''}`}
        data-part="spark"
        tabIndex={0}
        role="group"
        aria-label={`${label} over the window. Arrow keys mark a moment.`}
        onPointerMove={onMove}
        onPointerLeave={() => onIndex(null)}
        onKeyDown={onKeyDown}
        onBlur={() => onIndex(null)}
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

export function MovedTile({ answer, index, onIndex, readout, onOpen }: TileProps) {
  const latest = answer.bars.length - 1;

  return (
    <HealthTile tile="moved" label="Moved" readout={readout} answer={answer} index={index} onIndex={onIndex} onOpen={onOpen}>
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

export function InFlightTile({ answer, index, onIndex, readout, href, onOpen }: TileProps & { href: string }) {
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
      onOpen={onOpen}
    >
      <polygon className="hatch-health-band hatch-health-band--done" points={bands.done} />
      <polygon className="hatch-health-band hatch-health-band--inflight" points={bands.inFlight} />
      <polygon className="hatch-health-band hatch-health-band--waiting" points={bands.waiting} />
    </HealthTile>
  );
}

/** The Oldest tile. `onOpenPast` opens its dialog with the list narrowed to past normal; one issue past normal is a
    link to it instead, and none is plain text. The warning leads only at rest: on a marked bar the line is a time. */
export function OldestTile({ answer, index, onIndex, readout, onOpen, onOpenPast }: TileProps & { onOpenPast?: () => void }) {
  const bands = ageBandPolygons(answer, WIDTH, HEIGHT);
  const issue = index === null ? oldestIssue(answer) : null;
  const warning = index === null ? warningLead(answer) : { kind: 'none' as const };
  const lead: TileLead | undefined =
    warning.kind === 'issue'
      ? { to: warning.href, label: warning.label }
      : warning.kind === 'list' && onOpenPast
        ? { onPress: onOpenPast, label: warning.label }
        : undefined;

  return (
    <HealthTile
      tile="oldest"
      label="Oldest in flight"
      readout={readout}
      answer={answer}
      index={index}
      onIndex={onIndex}
      onOpen={onOpen}
      lead={lead}
      detailHref={issue ? `/issues/${encodeURIComponent(issue.key)}` : undefined}
      peek={issue ? issuePeek(issue) : undefined}
    >
      <polygon className="hatch-health-band hatch-health-band--healthy" points={bands.healthy} />
      <polygon className="hatch-health-band hatch-health-band--aging" points={bands.aging} />
      <polygon className="hatch-health-band hatch-health-band--past" points={bands.pastNormal} />
      {bands.noNormal && <polygon className="hatch-health-band hatch-health-band--nonormal" points={bands.noNormal} />}
    </HealthTile>
  );
}

export function WaitingTile({ answer, index, onIndex, readout, onOpen }: TileProps) {
  const steps = waitingSteps(answer, WIDTH, HEIGHT);

  return (
    <HealthTile
      tile="waiting"
      label="Waiting on you"
      readout={readout}
      answer={answer}
      index={index}
      onIndex={onIndex}
      onOpen={onOpen}
    >
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

/** One lane cell, as the tile and the Runners dialog both draw it. `title` is its hover text; a legend
    swatch leaves it off. */
export function LaneCell({ cell, title }: { cell: BoardHealthCell; title?: string }) {
  return <span className={cellClass(cell)} title={title} />;
}

/**
 * The Runners tile: a thin lane per runner, at most eight - its dialog draws them all - drawn the full width
 * of the tile with no names - the hover list and each cell's title name them -
 * and a legend one press away. Its body replaces the svg sparkline
 * with 24 HTML cells a row - the svg would stretch hollow and striped cells
 * into a skew - so its moment marker is its own, `hatch-health-lane-moment`,
 * not the shared `hatch-health-moment` the other four tiles count on.
 */
export function RunnersTile({ answer, index, onIndex, readout, onOpen }: TileProps) {
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
      <div className="hatch-health-lane-grid">
        {lanes.map((lane) => (
          <div className="hatch-health-lane-cells" key={lane.name}>
            {lane.cells.map((cell, i) => (
              <LaneCell key={i} cell={cell} title={`${lane.name}: ${cellWords(cell)}`} />
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
      onOpen={onOpen}
    />
  );
}
