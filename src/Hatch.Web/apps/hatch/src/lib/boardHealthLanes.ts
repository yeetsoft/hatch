/* The Runners tile's own maths and wording, apart from the component - the
   same split lib/boardHealth.ts makes for the other four tiles. Everything
   here reads a `BoardHealth` answer's `runners` block and nothing else. */

import { formatSpan } from './cardAge';
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

/** Every lane the read answers, whole: the dialog draws them all, where the
    tile caps them at `MAX_LANES`. */
export const allLanes = (answer: BoardHealth): BoardHealthLane[] => answer.runners.lanes;

/** Where a runner stands now, in a word: paused beats up, and a runner that is
    neither is "not up". */
export function laneStanding(lane: BoardHealthLane): string {
  if (lane.paused) return 'paused';
  return lane.up ? 'up' : 'not up';
}

const clockTime = (at: string, locale?: string): string =>
  new Date(at).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' });

/** When a runner was last heard from, against the window's end, written as the
    axis writes the time. Under a minute (or a last-seen after the end) it says
    "at"; otherwise how long before. Reads no clock. */
export function lastHeardWords(lane: BoardHealthLane, answer: BoardHealth, locale?: string): string {
  const end = new Date(answer.to).getTime();
  const gap = Math.floor((end - new Date(lane.lastSeenAt).getTime()) / 1000);
  const time = clockTime(answer.to, locale);

  return gap < 60 ? `last heard at ${time}` : `last heard ${formatSpan(gap)} before ${time}`;
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
export const LANE_LEGEND: { title: string; body: string; state: BoardHealthCell['state'] }[] = [
  { title: 'Moved a ticket', body: 'an increment ended with a move', state: 'moved' },
  { title: 'Working', body: 'in an increment, or ended one without a move', state: 'working' },
  { title: 'Let go or errored', body: 'a stall, a drop or a failed session', state: 'let-go' },
  { title: 'Out of usage or agent down', body: 'from the state record', state: 'down' },
  { title: 'Idle', body: 'up, nothing it could take', state: 'idle' },
  { title: 'Paused', body: 'from the state record', state: 'paused' },
  { title: 'Off', body: 'no session open', state: 'off' },
];

/** The Runners dialog's sentence for the marked bar: each runner's state there,
    opening on the bar's time. At rest it counts the runners instead. Every lane
    is named - none is "not drawn". */
export function lanesMarkedWords(answer: BoardHealth, index: number | null, locale?: string): string {
  const { up, paused, withLane, lanes } = answer.runners;
  const bar = index === null ? null : (answer.bars[index] ?? null);

  if (bar === null || index === null) {
    return `${withLane} ${withLane === 1 ? 'runner' : 'runners'}, ${up} up, ${paused} paused`;
  }

  const states = lanes.map((lane) => `${lane.name} ${cellWords(lane.cells[index])}`).join(', ');

  return `${momentLabel(bar.start, answer.barMinutes, locale)}: ${states}`;
}
