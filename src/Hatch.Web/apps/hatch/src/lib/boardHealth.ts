/* The board health strip's maths and every sentence it says, apart from the
   component - the same split lib/cumulativeFlow.ts makes from its chart: this
   app has no DOM test setup, so what can be wrong lives in a function with a
   test beside it.

   Everything here reads a `BoardHealth` answer and nothing else. The four
   tiles (moved, in flight, oldest in flight, waiting on you) and the strip's
   three charts share one axis: bar `i` of 24 owns slot `i`, and every point a
   chart draws for that bar sits at the slot's centre, so a mark on one chart
   lines up with the same bar on the others.

   It imports `momentLabel` from ./cumulativeFlow, `formatSpan` from ./cardAge,
   `withWindow` from ./reportWindow and `scopeSearch` from ./scope, and copies
   none of them. */

import { runnersTileWords } from './boardHealthLanes';
import { formatSpan } from './cardAge';
import { momentLabel } from './cumulativeFlow';
import { withWindow } from './reportWindow';
import { SCOPE, scopeSearch } from './scope';
import type { BoardHealth, BoardHealthAges, BoardHealthBar, BoardHealthIssue } from '../types';

/** The window lengths the strip offers, shortest first. */
export const HEALTH_WINDOWS = ['2h', '4h', '8h', '12h', '24h'] as const;

export type HealthWindow = (typeof HEALTH_WINDOWS)[number];

/** The window the strip opens on, and what any unknown value falls back to. */
export const DEFAULT_HEALTH_WINDOW: HealthWindow = '8h';

/** `raw` if it is one of the offered windows, else the default - never a
    refusal, so a stale stored value or a mistyped one opens the strip. */
export const parseHealthWindow = (raw: string | null | undefined): HealthWindow =>
  HEALTH_WINDOWS.find((choice) => choice === raw) ?? DEFAULT_HEALTH_WINDOW;

/** How many bars a window is cut into. */
export const BARS = 24;

/** Whether there is anything to draw: no bars means no issue is in scope yet,
    and the strip is left out rather than drawn as a placeholder. */
export const hasStrip = (answer: BoardHealth): boolean => answer.bars.length > 0;

/** The window's own label, from the answer alone: `8h` for twenty-four bars of
    twenty minutes. */
export const windowLabel = (answer: BoardHealth): string => `${Math.round((answer.barMinutes * BARS) / 60)}h`;

/** The strip's axis in one sentence: `to 15:40 · 20 min bars`. The time is the
    window's end in the reader's zone, in the form `momentLabel` writes it. */
export function axisSentence(answer: BoardHealth, locale?: string): string {
  const time = new Date(answer.to).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });

  return `to ${time} · ${answer.barMinutes} min bars`;
}

// ---- Geometry ----

export const round = (n: number): number => Math.round(n * 100) / 100;

/** One bar's slot on the shared axis. */
export interface Slot {
  x: number;
  width: number;
}

/** The twenty-four equal slots across `width`, left to right. Bar `i` owns
    slot `i`. */
export function barSlots(width: number): Slot[] {
  const slot = width / BARS;

  return Array.from({ length: BARS }, (_, i) => ({ x: round(i * slot), width: round(slot) }));
}

/** The bar under pixel `x` of a `width`-wide strip, clamped so that `x ==
    width` is the last bar. Null outside the strip, or when it has no width. */
export function barIndexAt(x: number, width: number): number | null {
  if (!(width > 0) || !(x >= 0) || x > width) return null;

  return Math.min(BARS - 1, Math.floor((x / width) * BARS));
}

/** The centre of bar `index`'s slot: where every chart puts that bar's point. */
export const momentX = (index: number, width: number): number => round(((index + 0.5) * width) / BARS);

/** A bar's drawn share of its slot, so neighbours do not touch. */
export const BAR_FILL = 0.8;

/** One moved bar as a rectangle. */
export interface MovedRect {
  index: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

/** The scale every chart divides by: the tallest value, and never under 1, so
    an all-zero window divides by one and not by zero. */
export const scaleOf = (values: number[]): number => Math.max(1, ...values);

/** The moved counts as rectangles, scaled to the tallest bar. A bar that moved
    nothing has no height; any other is never under one unit high, so a single
    move is still seen beside a tall one. `[]` for an answer with no bars. */
export function movedBars(answer: BoardHealth, width: number, height: number): MovedRect[] {
  const tallest = scaleOf(answer.bars.map((bar) => bar.moved));
  const slot = width / BARS;

  return answer.bars.map((bar, index) => {
    const drawn = bar.moved > 0 ? Math.min(height, Math.max(1, (bar.moved / tallest) * height)) : 0;

    return {
      index,
      x: round(momentX(index, width) - (slot * BAR_FILL) / 2),
      y: round(height - drawn),
      width: round(slot * BAR_FILL),
      height: round(drawn),
    };
  });
}

/** The three bands of the state chart, each an SVG `points` string. */
export interface BandPolygons {
  done: string;
  inFlight: string;
  waiting: string;
}

const pointsOf = (points: [number, number][]): string => points.map(([x, y]) => `${x},${y}`).join(' ');

/** How much of a bar one layer of a stack holds. */
type Layer = (bar: BoardHealthBar) => number;

/**
 * Stacks `layers`, bottom first, one band each, scaled to the tallest bar's
 * whole stack. Each band's string is its upper edge left to right, then its own
 * lower edge back, every point at `momentX`. The scale never falls under 1, so
 * an all-zero window stays finite. Empty strings for an answer with no bars.
 */
function stackBands(answer: BoardHealth, width: number, height: number, layers: Layer[]): string[] {
  if (answer.bars.length === 0) return layers.map(() => '');

  const below = (bar: BoardHealthBar, count: number): number =>
    layers.slice(0, count).reduce((sum, layer) => sum + layer(bar), 0);
  const scale = scaleOf(answer.bars.map((bar) => below(bar, layers.length)));
  const y = (value: number) => round(height - (value / scale) * height);
  const xs = answer.bars.map((_, i) => momentX(i, width));

  return layers.map((_, k) => {
    const top = answer.bars.map((bar, i): [number, number] => [xs[i], y(below(bar, k + 1))]);
    const bottom = answer.bars.map((bar, i): [number, number] => [xs[i], y(below(bar, k))]).reverse();

    return pointsOf([...top, ...bottom]);
  });
}

/**
 * Where work stood, as three stacked bands: done at the bottom, in flight above
 * it, waiting on top. Scaled to the tallest of the three stacked, so the top
 * band's upper edge is the whole count at each bar.
 */
export function bandPolygons(answer: BoardHealth, width: number, height: number): BandPolygons {
  const [done, inFlight, waiting] = stackBands(answer, width, height, [
    (bar) => bar.done,
    (bar) => bar.inFlight,
    (bar) => bar.waiting,
  ]);

  return { done, inFlight, waiting };
}

/** The oldest tile's four bands, each an SVG `points` string. */
export interface AgeBandPolygons {
  pastNormal: string;
  aging: string;
  healthy: string;
  /** Empty unless some bar has an issue in a column with no normal yet. */
  noNormal: string;
}

/**
 * How old the in-flight issues were at each bar, as stacked bands: past normal
 * on the baseline, aging above it, healthy above that, and - only where a
 * column has no normal yet - a neutral band on top. Scaled to the tallest
 * stack, so a bar with nothing in flight draws nothing.
 */
export function ageBandPolygons(answer: BoardHealth, width: number, height: number): AgeBandPolygons {
  const [pastNormal, aging, healthy, noNormal] = stackBands(answer, width, height, [
    (bar) => bar.ages.pastNormal,
    (bar) => bar.ages.aging,
    (bar) => bar.ages.healthy,
    (bar) => bar.ages.noNormal,
  ]);

  return { pastNormal, aging, healthy, noNormal: answer.bars.some((bar) => bar.ages.noNormal > 0) ? noNormal : '' };
}

/** A point on a chart, or null where there is none to mark. */
export interface Dot {
  x: number;
  y: number;
}

/** What waits on a person at one bar: pull requests plus questions. */
export const waitingCount = (bar: BoardHealthBar): number => bar.waitingPullRequests + bar.waitingQuestions;

/** The waiting-on-you chart: a step line, the area under it, and the last
    bar's point for a dot. */
export interface WaitingSteps {
  line: string;
  area: string;
  last: Dot | null;
}

/**
 * What waits on a person at each bar as a step path - level at one bar's value
 * until the next bar's point, then straight up or down to that one's - scaled to
 * the largest count. `area` closes the line down to the floor. Empty strings
 * and null for an answer with no bars.
 */
export function waitingSteps(answer: BoardHealth, width: number, height: number): WaitingSteps {
  if (answer.bars.length === 0) return { line: '', area: '', last: null };

  const scale = scaleOf(answer.bars.map(waitingCount));
  const points = answer.bars.map((bar, i): Dot => ({
    x: momentX(i, width),
    y: round(height - (waitingCount(bar) / scale) * height),
  }));

  const line = points
    .map((point, i) => (i === 0 ? `M ${point.x} ${point.y}` : `H ${point.x} V ${point.y}`))
    .join(' ');
  const area = `${line} V ${round(height)} H ${points[0].x} Z`;

  return { line, area, last: points[points.length - 1] };
}

// ---- Readouts ----

export type Tile = 'moved' | 'inFlight' | 'oldest' | 'waiting' | 'runners';

/** How a tile's colour follows its meaning. Never the only carrier of it: every
    tone has a glyph or a word beside it. */
export type Tone = 'good' | 'neutral' | 'warn';

/** Which way the window moved against the one before it. */
export type Arrow = 'up' | 'down' | 'flat';

/** The text of one tile. `glyph` is its own field, not part of `second`, so a
    screen reader's sentence can say the arrow in words. */
export interface TileReadout {
  value: string;
  second: string;
  /** The hover's extra line; empty when there is none. */
  detail: string;
  tone: Tone;
  /** `▲`, `▼`, `–` or `⚠`; empty when the tile has none. */
  glyph: string;
  /** Set only on the moved tile at rest. */
  arrow: Arrow | null;
  /** What a screen reader hears after `detail` and a sighted reader does not
      need said again; set only on the oldest tile at rest. */
  spoken?: string;
}

export type Readouts = Record<Tile, TileReadout>;

const ARROW_GLYPH: Record<Arrow, string> = { up: '▲', down: '▼', flat: '–' };

const NONE: BoardHealthBar = {
  start: '',
  end: '',
  board: 0,
  runner: 0,
  person: 0,
  moved: 0,
  waiting: 0,
  inFlight: 0,
  done: 0,
  waitingPullRequests: 0,
  waitingQuestions: 0,
  oldestInFlightSeconds: null,
  oldestWaitSeconds: null,
  ages: { healthy: 0, aging: 0, pastNormal: 0, noNormal: 0 },
};

export const plural = (n: number, one: string, many: string): string => `${n} ${n === 1 ? one : many}`;

/** The moved tile, at rest or on one bar. */
function moved(answer: BoardHealth, bar: BoardHealthBar | null, locale?: string): TileReadout {
  if (bar) {
    return {
      value: String(bar.moved),
      second: momentLabel(bar.start, answer.barMinutes, locale),
      detail: `${bar.runner} by runners, ${bar.person} by a person, ${bar.board} by the board`,
      tone: 'neutral',
      glyph: '',
      arrow: null,
    };
  }

  const change = answer.total - answer.previousTotal;
  const arrow: Arrow = change > 0 ? 'up' : change < 0 ? 'down' : 'flat';
  const label = windowLabel(answer);

  return {
    value: String(answer.total),
    second: change === 0 ? `same as prior ${label}` : `${Math.abs(change)} vs prior ${label}`,
    detail: '',
    tone: arrow === 'up' ? 'good' : arrow === 'down' ? 'warn' : 'neutral',
    glyph: ARROW_GLYPH[arrow],
    arrow,
  };
}

/** The in-flight tile: the three counts at the bar, or at the last bar - now. */
function inFlight(answer: BoardHealth, shown: BoardHealthBar, onBar: boolean, locale?: string): TileReadout {
  return {
    value: String(shown.inFlight),
    second: `${shown.waiting} waiting · ${shown.done} done`,
    detail: onBar ? momentLabel(shown.start, answer.barMinutes, locale) : '',
    tone: 'neutral',
    glyph: '',
    arrow: null,
  };
}

/** The oldest issue in flight now, or null with none. The read lists them
    longest stay first. */
export const oldestIssue = (answer: BoardHealth): BoardHealthIssue | null => answer.inFlightIssues[0] ?? null;

/** The split of in-flight issues by age, in words:
    `2 past normal · 3 aging · 9 healthy`. Issues in a column with no normal
    yet are added as `· 1 no normal yet`, and only when there are any. */
export function splitSentence(ages: BoardHealthAges): string {
  const parts = [`${ages.pastNormal} past normal`, `${ages.aging} aging`, `${ages.healthy} healthy`];
  if (ages.noNormal > 0) parts.push(`${ages.noNormal} no normal yet`);

  return parts.join(' · ');
}

/** What pointing at the oldest issue says, one line each. */
export interface IssuePeek {
  /** Its key and title. */
  heading: string;
  /** How long it has stood in its column. */
  stood: string;
  /** The column's normal. */
  normal: string;
}

/** The oldest issue's peek: `HA-12 The title`, `Stood 4h 10m in In Progress`,
    `normal 2h` - or `no normal yet` for a column without one. */
export function issuePeek(issue: BoardHealthIssue): IssuePeek {
  return {
    heading: `${issue.key} ${issue.title}`,
    stood: `Stood ${formatSpan(issue.seconds)} in ${issue.statusName}`,
    normal: issue.normalSeconds === null ? 'no normal yet' : `normal ${formatSpan(issue.normalSeconds)}`,
  };
}

/** The oldest-in-flight tile. `⚠` and the warning tone are the window's, so
    they show at rest only. At rest the detail line names the oldest issue; on a
    bar it says that bar's split. */
function oldest(answer: BoardHealth, shown: BoardHealthBar, onBar: boolean, locale?: string): TileReadout {
  const value = shown.oldestInFlightSeconds === null ? 'none' : formatSpan(shown.oldestInFlightSeconds);

  if (onBar) {
    return {
      value,
      second: momentLabel(shown.start, answer.barMinutes, locale),
      detail: splitSentence(shown.ages),
      tone: 'neutral',
      glyph: '',
      arrow: null,
    };
  }

  const past = answer.pastNormal > 0;
  const issue = oldestIssue(answer);

  return {
    value,
    second: past ? `${answer.pastNormal} past normal` : 'none past normal',
    detail: issue ? `${issue.key} · ${issue.statusName}` : '',
    tone: past ? 'warn' : 'neutral',
    glyph: past ? '⚠' : '',
    arrow: null,
    spoken: splitSentence(shown.ages),
  };
}

/** The Runners tile: how many lanes are up, and what each was doing at rest
    or on one bar - the words alone come from lib/boardHealthLanes.ts. */
function runners(answer: BoardHealth, index: number | null, locale?: string): TileReadout {
  const words = runnersTileWords(answer, index, locale);

  return { value: words.value, second: words.second, detail: words.detail, tone: 'neutral', glyph: '', arrow: null };
}

/** The waiting-on-you tile. */
function waiting(answer: BoardHealth, shown: BoardHealthBar, onBar: boolean, locale?: string): TileReadout {
  const count = waitingCount(shown);
  const base = { value: String(count), tone: 'neutral' as const, glyph: '', arrow: null };

  if (onBar) {
    return {
      ...base,
      second: momentLabel(shown.start, answer.barMinutes, locale),
      detail:
        count === 0
          ? 'nothing waiting'
          : `${plural(shown.waitingPullRequests, 'pull request', 'pull requests')} · ${plural(shown.waitingQuestions, 'question', 'questions')}`,
    };
  }

  const second =
    count === 0
      ? 'nothing waiting'
      : shown.oldestWaitSeconds === null
        ? 'oldest unknown'
        : `oldest ${formatSpan(shown.oldestWaitSeconds)}`;

  return { ...base, second, detail: '' };
}

/**
 * What each tile says. `null` is the window at rest: the moved tile gives the
 * window's total against the one before, and the other three read the last bar,
 * which is now. An index is that bar, and every tile gives its figure at it.
 * An index with no bar behind it reads as rest.
 */
export function readout(answer: BoardHealth, index: number | null, locale?: string): Readouts {
  const bar = index === null ? null : (answer.bars[index] ?? null);
  const shown = bar ?? answer.bars[answer.bars.length - 1] ?? { ...NONE, start: answer.from };
  const onBar = bar !== null;

  return {
    moved: moved(answer, bar, locale),
    inFlight: inFlight(answer, shown, onBar, locale),
    oldest: oldest(answer, shown, onBar, locale),
    waiting: waiting(answer, shown, onBar, locale),
    runners: runners(answer, index, locale),
  };
}

/** Each tile's name, as a sentence opens on it. */
export const tileName: Record<Tile, string> = {
  moved: 'Moved',
  inFlight: 'In flight',
  oldest: 'Oldest in flight',
  waiting: 'Waiting on you',
  runners: 'Runners',
};

/** The moved tile's change in words: `up 16 against the prior 8h`. */
const changeSentence = (readout: TileReadout): string => {
  if (readout.arrow === 'flat') return readout.second.replace('same as prior', 'same as the prior');

  return `${readout.arrow} ${readout.second.replace(' vs prior ', ' against the prior ')}`;
};

/** One tile as one sentence for `aria-label`, readable alone: `Moved: 47, up 16
    against the prior 8h`. Arrows and the warning glyph are said in words.
    `hover` carries the Runners tile's hover list, said last, when a bar is
    marked. */
export function tileLabel(tile: Tile, readout: TileReadout, hover?: string): string {
  const parts = [`${tileName[tile]}: ${readout.value}`];
  parts.push(tile === 'moved' && readout.arrow ? changeSentence(readout) : readout.second);
  if (readout.detail) parts.push(readout.detail);
  if (readout.spoken) parts.push(readout.spoken);
  if (hover) parts.push(hover);

  return parts.filter(Boolean).join(', ');
}

// ---- Compact readout ----

/** One tile as the phone's closed row draws it: the figure, and a short second
    line under it. Display only - the row's spoken label is `tileLabel` on the
    full readout, so a screen reader hears the whole sentence. */
export interface CompactReadout {
  value: string;
  second: string;
  tone: Tone;
}

export type CompactReadouts = Record<Tile, CompactReadout>;

/** The shortened second line. Each rule matches the one sentence `readout`
    writes at rest, and falls back to the full line if that sentence ever
    changes, so a rewording there shows long here rather than wrong. */
function shorten(tile: Tile, readout: TileReadout): string {
  const { second, glyph } = readout;

  switch (tile) {
    case 'moved': {
      if (readout.arrow === 'flat') return `${glyph} same`;
      const change = /^(\d+) vs prior /.exec(second);
      return change ? `${glyph} ${change[1]}` : second;
    }
    case 'inFlight': {
      const done = /(\d+) done$/.exec(second);
      return done ? `${done[1]} done` : second;
    }
    case 'oldest': {
      if (second === 'none past normal') return 'none old';
      const past = /^(\d+) past normal$/.exec(second);
      return past ? `${past[1]} old` : second;
    }
    case 'waiting': {
      if (second === 'nothing waiting') return 'none';
      if (second === 'oldest unknown') return 'unknown';
      const age = /^oldest (.+)$/.exec(second);
      return age ? `${age[1]} old` : second;
    }
    // The Runners tile splits its value instead - see compactReadout.
    case 'runners':
      return second;
  }
}

/**
 * What each tile says in the phone's closed row. Built from the window at rest,
 * `readout(answer, null)`, so its numbers are the full tiles' numbers; only the
 * second line is shortened.
 *
 * The Runners tile has no room for both its value and "up"/"paused": its
 * value, "3 of 4", splits instead into "3" over "of 4".
 */
export function compactReadout(answer: BoardHealth, locale?: string): CompactReadouts {
  const full = readout(answer, null, locale);
  const one = (tile: Tile): CompactReadout => ({
    value: full[tile].value,
    second: shorten(tile, full[tile]),
    tone: full[tile].tone,
  });

  const split = full.runners.value.indexOf(' ');
  const runnersCompact: CompactReadout = {
    value: split === -1 ? full.runners.value : full.runners.value.slice(0, split),
    second: split === -1 ? '' : full.runners.value.slice(split + 1),
    tone: full.runners.tone,
  };

  return {
    moved: one('moved'),
    inFlight: one('inFlight'),
    oldest: one('oldest'),
    waiting: one('waiting'),
    runners: runnersCompact,
  };
}

// ---- Keyboard ----

/**
 * The marked bar after `key`, for a keyboard on the strip. `ArrowLeft` and
 * `ArrowRight` step one bar and stop at the ends; from no bar, `ArrowLeft` lands
 * on the last and `ArrowRight` on the first. `Home` and `End` jump to the first
 * and the last, `Escape` clears the mark, and any other key leaves `current`
 * as it was. With no bars to mark, a movement key gives null.
 */
export function stepMoment(current: number | null, key: string, count: number): number | null {
  switch (key) {
    case 'Escape':
      return null;
    case 'ArrowLeft':
      return count <= 0 ? null : current === null ? count - 1 : Math.max(0, current - 1);
    case 'ArrowRight':
      return count <= 0 ? null : current === null ? 0 : Math.min(count - 1, current + 1);
    case 'Home':
      return count <= 0 ? null : 0;
    case 'End':
      return count <= 0 ? null : count - 1;
    default:
      return current;
  }
}

// ---- Link ----

/** The cumulative flow report on the strip's own two ends, under the same
    scope: `/reports/cumulative-flow?scope=AER,FIL&from=…&to=…`. */
export function flowHref(answer: BoardHealth, scopeKeys: string[]): string {
  const params = new URLSearchParams();
  if (scopeKeys.length > 0) params.set(SCOPE, scopeKeys.join(','));

  const ends = {
    from: new Date(answer.from).toISOString(),
    to: new Date(answer.to).toISOString(),
  };

  return `/reports/cumulative-flow${scopeSearch(withWindow(params, ends))}`;
}
