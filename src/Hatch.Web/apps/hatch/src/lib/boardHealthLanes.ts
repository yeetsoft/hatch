/* The Runners tile's own maths and wording, apart from the component - the
   same split lib/boardHealth.ts makes for the other four tiles. Everything
   here reads a `BoardHealth` answer's `runners` block and nothing else. */

import { momentLabel } from './cumulativeFlow';
import type { BoardHealth, BoardHealthCell, BoardHealthLane } from '../types';

/** At most this many lanes are ever drawn; the read sorts up now first, then
    most recently heard from, so the first `MAX_LANES` are the ones worth
    drawing. */
export const MAX_LANES = 8;

export interface DrawnLanes {
  lanes: BoardHealthLane[];
  /** How many of the read's lanes were left out. */
  omitted: number;
}

/** The lanes the tile draws, and how many it left out. The read is already
    sorted, so this only caps it. */
export function drawnLanes(answer: BoardHealth): DrawnLanes {
  const lanes = answer.runners.lanes;

  return { lanes: lanes.slice(0, MAX_LANES), omitted: Math.max(0, lanes.length - MAX_LANES) };
}

const LET_GO_WORDS: Record<string, string> = {
  dropped: 'dropped a ticket',
  lapsed: 'lapsed',
  stalled: 'stalled',
  errored: 'errored',
};

/** The wording `lib/runners.ts` uses for the same facts, lower-cased. */
const DOWN_WORDS: Record<string, string> = {
  'out-of-usage': 'out of usage',
  'not-starting': 'agent not starting',
  'signed-out': 'agent needs signing in',
};

/** One cell's words, from its state and why - the hover list's and the
    tile's own label's shared sentence for a runner at a bar. */
export function cellWords(cell: BoardHealthCell): string {
  switch (cell.state) {
    case 'moved':
      return 'moved a ticket';
    case 'working':
      return 'working';
    case 'idle':
      return 'idle';
    case 'off':
      return 'off';
    case 'paused':
      return cell.why === 'board' ? 'paused by the board' : 'paused';
    case 'let-go':
      return (cell.why && LET_GO_WORDS[cell.why]) || 'let go';
    case 'down':
      return (cell.why && DOWN_WORDS[cell.why]) || 'agent down';
    case 'blank':
      return 'no record yet';
  }
}

export interface RunnersTileWords {
  value: string;
  second: string;
  detail: string;
}

/** The Runners tile's own value, second line and detail - at rest (`index`
    null) or on one bar. */
export function runnersTileWords(answer: BoardHealth, index: number | null, locale?: string): RunnersTileWords {
  const { up, paused, withLane } = answer.runners;
  const { omitted } = drawnLanes(answer);
  const bar = index === null ? null : (answer.bars[index] ?? null);

  if (bar === null || index === null) {
    const second = withLane === 0 ? 'none up' : paused > 0 ? `up · ${paused} paused` : 'up';
    return { value: `${up} of ${withLane}`, second, detail: omitted > 0 ? `${omitted} not drawn` : '' };
  }

  const { lanes } = drawnLanes(answer);
  const active = lanes.filter((lane) => lane.cells[index].state !== 'off' && lane.cells[index].state !== 'blank');
  const pausedNow = lanes.filter((lane) => lane.cells[index].state === 'paused').length;

  return {
    value: `${active.length} of ${withLane}`,
    second: momentLabel(bar.start, answer.barMinutes, locale),
    detail: pausedNow > 0 ? `${pausedNow} paused` : '',
  };
}

export interface HoverLine {
  name: string;
  words: string;
}

export interface HoverList {
  lines: HoverLine[];
  omitted: number;
}

/** The hover popover's own lines: each drawn runner's name and what it was
    doing at `index`'s bar, then how many were left out. */
export function hoverList(answer: BoardHealth, index: number): HoverList {
  const { lanes, omitted } = drawnLanes(answer);

  return { lines: lanes.map((lane) => ({ name: lane.name, words: cellWords(lane.cells[index]) })), omitted };
}

/** The legend's seven lines, the mock-up's own words. */
export const LANE_LEGEND: { title: string; body: string }[] = [
  { title: 'Moved a ticket', body: 'an increment ended with a move' },
  { title: 'Working', body: 'in an increment, or ended one without a move' },
  { title: 'Let go or errored', body: 'a stall, a drop or a failed session' },
  { title: 'Out of usage or agent down', body: 'from the state record' },
  { title: 'Idle', body: 'up, nothing it could take' },
  { title: 'Paused', body: 'from the state record' },
  { title: 'Off', body: 'no session open' },
];
