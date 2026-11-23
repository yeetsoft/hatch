/* The maths and the words of the dialogs the health tiles open: the same
   window as the tile's sparkline, drawn large, with an axis, a legend and the
   detail the read already holds. Pure, like lib/boardHealth.ts beside it, and
   for the same reason: this app has no DOM test setup, so what can be wrong
   lives in a function with a test.

   Everything reads a `BoardHealth` answer and nothing else. Every function
   that draws takes the width and height of the box it draws into, because the
   dialog measures its own svg and draws at that size - 1:1, never stretched, so
   a fill pattern keeps its angle.

   It imports the tile's own helpers from ./boardHealth and copies none of
   them: `barSlots`-style slots, `momentX`, `scaleOf`, `BAR_FILL`, `plural`. */

import {
  BARS,
  BAR_FILL,
  momentX,
  plural,
  round,
  scaleOf,
  tileName,
  waitingCount,
  windowLabel,
} from './boardHealth';
import type { Tile } from './boardHealth';
import { formatSpan } from './cardAge';
import { momentLabel } from './cumulativeFlow';
import type { BoardHealth, BoardHealthBar } from '../types';

/** The four tiles that open a dialog. */
export type DialogTile = Exclude<Tile, 'runners'>;

/** The dialog's title: the tile's own label and the window, `Moved, last 8h`.
    It follows the answer's window, not the button that was pressed, so the
    chart and its title agree while a new window loads. */
export const dialogTitle = (tile: Tile, answer: BoardHealth): string =>
  `${tileName[tile]}, last ${windowLabel(answer)}`;

// ---- Marks ----

/** Every mark a dialog's chart draws and its legend names. */
export type MarkKey =
  | 'runner'
  | 'person'
  | 'board'
  | 'done'
  | 'inFlight'
  | 'waiting'
  | 'line'
  | 'now'
  | 'pullRequests'
  | 'questions';

/** How a filled mark is told apart from its neighbours by more than its shade. */
export type PatternKind = 'solid' | 'stripes' | 'dots';

/** The fill pattern of the marks that stack side by side in one chart. In
    flight's bands are told apart by their order and the labelled legend. */
export const MARK_PATTERN: Partial<Record<MarkKey, PatternKind>> = {
  runner: 'solid',
  person: 'stripes',
  board: 'dots',
  pullRequests: 'solid',
  questions: 'stripes',
};

export interface LegendEntry {
  key: MarkKey;
  label: string;
}

/** Every mark of a tile's chart, top to bottom as it is drawn. Runners' own
    legend is the next dialog's. */
export function legend(tile: Tile): LegendEntry[] {
  switch (tile) {
    case 'moved':
      return [
        { key: 'board', label: 'The board' },
        { key: 'person', label: 'A person' },
        { key: 'runner', label: 'Runners' },
      ];
    case 'inFlight':
      return [
        { key: 'waiting', label: 'Waiting' },
        { key: 'inFlight', label: 'In flight' },
        { key: 'done', label: 'Done' },
      ];
    case 'oldest':
      return [
        { key: 'line', label: 'Age of the oldest in flight' },
        { key: 'now', label: 'Now' },
      ];
    case 'waiting':
      return [
        { key: 'questions', label: 'Questions' },
        { key: 'pullRequests', label: 'Pull requests' },
      ];
    case 'runners':
      return [];
  }
}

// ---- Axis and scale ----

/** One label on the time axis, `x` in the chart's own units from its left edge. */
export interface AxisTick {
  x: number;
  label: string;
}

const clock = (at: string | number, locale?: string): string =>
  new Date(at).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });

/**
 * The times along the bottom: the start of bars 0, 6, 12 and 18, and the
 * window's end at the right edge. Each label is a clock time in the form
 * `axisSentence` writes it. `[]` for an answer with no bars.
 */
export function axisTicks(answer: BoardHealth, width: number, locale?: string): AxisTick[] {
  if (answer.bars.length === 0) return [];

  const ticks = [0, 6, 12, 18].flatMap((index): AxisTick[] => {
    const bar = answer.bars[index];
    return bar ? [{ x: round((index * width) / BARS), label: clock(bar.start, locale) }] : [];
  });

  return [...ticks, { x: round(width), label: clock(answer.to, locale) }];
}

/** The value at the top of the chart's scale, as the side text writes it. The
    scale is the one the tile's own sparkline divides by. `''` for Runners,
    which has no value scale. */
export function scaleTop(tile: Tile, answer: BoardHealth): string {
  switch (tile) {
    case 'moved':
      return String(scaleOf(answer.bars.map((bar) => bar.moved)));
    case 'inFlight':
      return String(scaleOf(answer.bars.map((bar) => bar.done + bar.inFlight + bar.waiting)));
    case 'oldest':
      return formatSpan(scaleOf(answer.bars.map((bar) => bar.oldestInFlightSeconds ?? 0)));
    case 'waiting':
      return String(scaleOf(answer.bars.map(waitingCount)));
    case 'runners':
      return '';
  }
}

// ---- Moved ----

export type MovedSource = 'runner' | 'person' | 'board';

/** One source's share of a bar, as a rectangle. */
export interface MovedSegment {
  source: MovedSource;
  count: number;
  y: number;
  height: number;
}

/** One moved bar, stacked: runners at the bottom, then a person, then the
    board. A source that moved nothing has no segment. */
export interface StackedBar {
  index: number;
  x: number;
  width: number;
  segments: MovedSegment[];
}

const STACK: MovedSource[] = ['runner', 'person', 'board'];

/**
 * The moved counts as stacked bars, scaled to the tallest bar's `moved`. A
 * segment that moved anything is never under one unit high, so a single move is
 * still seen beside a tall one; if those floors push a stack past the box, the
 * excess comes off its tallest segment. `[]` for an answer with no bars.
 */
export function stackedMovedBars(answer: BoardHealth, width: number, height: number): StackedBar[] {
  const tallest = scaleOf(answer.bars.map((bar) => bar.moved));
  const slot = width / BARS;

  return answer.bars.map((bar, index) => {
    const drawn = STACK.map((source) => ({ source, count: bar[source] })).filter((part) => part.count > 0);
    const heights = drawn.map((part) => Math.max(1, (part.count / tallest) * height));
    const excess = heights.reduce((sum, h) => sum + h, 0) - height;

    if (excess > 0) {
      const tall = heights.indexOf(Math.max(...heights));
      heights[tall] = Math.max(1, heights[tall] - excess);
    }

    let floor = height;
    const segments = drawn.map((part, i): MovedSegment => {
      floor -= heights[i];
      return { source: part.source, count: part.count, y: round(floor), height: round(heights[i]) };
    });

    return {
      index,
      x: round(momentX(index, width) - (slot * BAR_FILL) / 2),
      width: round(slot * BAR_FILL),
      segments,
    };
  });
}

/** What each source moved over the window, the window's total, and the total
    for the window before. The read holds no per-source figure for that one. */
export interface MovedTotals {
  runner: number;
  person: number;
  board: number;
  total: number;
  previousTotal: number;
}

export function movedTotals(answer: BoardHealth): MovedTotals {
  const sum = (pick: (bar: BoardHealthBar) => number): number => answer.bars.reduce((all, bar) => all + pick(bar), 0);

  return {
    runner: sum((bar) => bar.runner),
    person: sum((bar) => bar.person),
    board: sum((bar) => bar.board),
    total: sum((bar) => bar.moved),
    previousTotal: answer.previousTotal,
  };
}

export interface SummaryRow {
  key: string;
  label: string;
  value: string;
}

/** The Moved dialog's totals as rows, each one a label and a figure. */
export function movedSummary(answer: BoardHealth): SummaryRow[] {
  const totals = movedTotals(answer);
  const label = windowLabel(answer);

  return [
    { key: 'runner', label: 'By runners', value: String(totals.runner) },
    { key: 'person', label: 'By a person', value: String(totals.person) },
    { key: 'board', label: 'By the board', value: String(totals.board) },
    { key: 'total', label: `All, last ${label}`, value: String(totals.total) },
    { key: 'previous', label: `All, the ${label} before`, value: String(totals.previousTotal) },
  ];
}

// ---- In flight ----

/** Each band's count at the marked bar, or at the last bar - now - with none
    marked; top to bottom as the bands stack. */
export function bandCounts(answer: BoardHealth, index: number | null): SummaryRow[] {
  const bar = shownBar(answer, index);
  if (!bar) return [];

  return [
    { key: 'waiting', label: 'Waiting', value: String(bar.waiting) },
    { key: 'inFlight', label: 'In flight', value: String(bar.inFlight) },
    { key: 'done', label: 'Done', value: String(bar.done) },
  ];
}

// ---- Waiting on you ----

/** One of the waiting chart's two areas: its step edge, and the area closed
    around it. */
export interface WaitingBand {
  line: string;
  area: string;
}

export interface WaitingSplit {
  pullRequests: WaitingBand;
  questions: WaitingBand;
}

/**
 * What waits on a person as two stacked step areas: pull requests below,
 * questions above. The questions' upper edge is the whole count at each bar, so
 * it is the same line `waitingSteps` draws at the same size, and both areas
 * divide by the same scale. Empty strings for an answer with no bars.
 */
export function waitingSplit(answer: BoardHealth, width: number, height: number): WaitingSplit {
  const none: WaitingSplit = { pullRequests: { line: '', area: '' }, questions: { line: '', area: '' } };
  if (answer.bars.length === 0) return none;

  const scale = scaleOf(answer.bars.map(waitingCount));
  const xs = answer.bars.map((_, i) => momentX(i, width));
  const y = (value: number): number => round(height - (value / scale) * height);
  const floor = round(height);

  const stepLine = (ys: number[]): string =>
    ys.map((at, i) => (i === 0 ? `M ${xs[i]} ${at}` : `H ${xs[i]} V ${at}`)).join(' ');

  const lower = answer.bars.map((bar) => y(bar.waitingPullRequests));
  const upper = answer.bars.map((bar) => y(waitingCount(bar)));
  const lowerLine = stepLine(lower);
  const upperLine = stepLine(upper);

  /* Back along the lower edge, right to left: the forward step at bar `i` was
     "level, then up or down", so in reverse it is "down or up, then level". */
  const last = answer.bars.length - 1;
  let back = `V ${lower[last]}`;
  for (let i = last; i > 0; i--) back += ` H ${xs[i - 1]} V ${lower[i - 1]}`;

  return {
    pullRequests: { line: lowerLine, area: `${lowerLine} V ${floor} H ${xs[0]} Z` },
    questions: { line: upperLine, area: `${upperLine} ${back} Z` },
  };
}

/** How long the oldest thing waiting on a person has waited, at the last bar. */
export function oldestWaitWords(answer: BoardHealth): string {
  const bar = answer.bars[answer.bars.length - 1];
  if (!bar || waitingCount(bar) === 0) return 'nothing waiting';

  return bar.oldestWaitSeconds === null ? 'oldest wait unknown' : `oldest waited ${formatSpan(bar.oldestWaitSeconds)}`;
}

// ---- Words ----

/** The marked bar, or the last bar - now - with none marked or none behind the index. */
function shownBar(answer: BoardHealth, index: number | null): BoardHealthBar | null {
  const marked = index === null ? null : (answer.bars[index] ?? null);

  return marked ?? answer.bars[answer.bars.length - 1] ?? null;
}

/** A bar's figures in one sentence, for the line under the chart. With a bar
    marked it opens on that bar's time; at rest it is the window's own words -
    Moved gives its totals, the other three read the last bar, which is now. */
export function markedWords(tile: Tile, answer: BoardHealth, index: number | null, locale?: string): string {
  if (tile === 'runners' || answer.bars.length === 0) return '';

  const marked = index === null ? null : (answer.bars[index] ?? null);

  if (tile === 'moved' && marked === null) {
    const totals = movedTotals(answer);
    const label = windowLabel(answer);

    return `Last ${label}: ${totals.runner} by runners, ${totals.person} by a person, ${totals.board} by the board, ${totals.total} in all, against ${totals.previousTotal} in the ${label} before`;
  }

  const bar = marked ?? answer.bars[answer.bars.length - 1];
  const when = marked ? momentLabel(bar.start, answer.barMinutes, locale) : 'Now';

  switch (tile) {
    case 'moved':
      return `${when}: ${bar.runner} by runners, ${bar.person} by a person, ${bar.board} by the board`;
    case 'inFlight':
      return `${when}: ${bar.inFlight} in flight, ${bar.waiting} waiting, ${bar.done} done`;
    case 'oldest':
      return bar.oldestInFlightSeconds === null
        ? `${when}: nothing in flight`
        : `${when}: the oldest has been in flight ${formatSpan(bar.oldestInFlightSeconds)}`;
    case 'waiting': {
      if (waitingCount(bar) === 0) return `${when}: nothing waiting`;

      const parts = `${plural(bar.waitingPullRequests, 'pull request', 'pull requests')} and ${plural(bar.waitingQuestions, 'question', 'questions')} waiting`;

      return bar.oldestWaitSeconds === null
        ? `${when}: ${parts}, oldest wait unknown`
        : `${when}: ${parts}, oldest waited ${formatSpan(bar.oldestWaitSeconds)}`;
    }
  }
}
