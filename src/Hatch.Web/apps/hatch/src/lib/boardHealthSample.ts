/* A hand-built board health answer for the component tests - imported by tests
   alone, so it never reaches the bundle. Bar `i` is known from `i`: moved is
   `i % 5`, in flight is `2 + i % 4`, and what waits on a person is `i % 3` pull
   requests plus `i % 2` questions.

   `sampleRunners` covers every cell state at least once, across three lanes:
   `kestrel` (up, working its way through every non-paused state), `osprey`
   (paused by a person) and `heron` (gone, off throughout). */

import { BARS } from './boardHealth';
import type { BoardHealth, BoardHealthBar, BoardHealthCell, BoardHealthLane, BoardHealthRunners } from '../types';

const TO = Date.parse('2026-11-22T15:40:00.000Z');
const BAR_MS = 20 * 60_000;
const FROM = TO - BARS * BAR_MS;

export function sampleBar(i: number): BoardHealthBar {
  const moved = i % 5;
  const waitingPullRequests = i % 3;
  const waitingQuestions = i % 2;

  return {
    start: new Date(FROM + i * BAR_MS).toISOString(),
    end: new Date(FROM + (i + 1) * BAR_MS).toISOString(),
    board: 0,
    person: moved > 0 ? 1 : 0,
    runner: Math.max(0, moved - 1),
    moved,
    waiting: i % 3,
    inFlight: 2 + (i % 4),
    done: i,
    waitingPullRequests,
    waitingQuestions,
    oldestInFlightSeconds: 600 * (i + 1),
    oldestWaitSeconds: waitingPullRequests + waitingQuestions > 0 ? 7800 : null,
  };
}

/** Every cell state at least once, including every `why` a cell can carry. */
const CELL_CYCLE: BoardHealthCell[] = [
  { state: 'moved', why: null },
  { state: 'working', why: null },
  { state: 'idle', why: null },
  { state: 'off', why: null },
  { state: 'paused', why: null },
  { state: 'paused', why: 'board' },
  { state: 'down', why: 'out-of-usage' },
  { state: 'down', why: 'not-starting' },
  { state: 'down', why: 'signed-out' },
  { state: 'down', why: 'agent-down' },
  { state: 'down', why: null },
  { state: 'let-go', why: 'dropped' },
  { state: 'let-go', why: 'lapsed' },
  { state: 'let-go', why: 'stalled' },
  { state: 'let-go', why: 'errored' },
  { state: 'blank', why: null },
];

export function sampleRunners(over: Partial<BoardHealthRunners> = {}): BoardHealthRunners {
  const kestrel: BoardHealthLane = {
    name: 'kestrel',
    up: true,
    paused: false,
    lastSeenAt: new Date(TO).toISOString(),
    cells: Array.from({ length: BARS }, (_, i) => CELL_CYCLE[i % CELL_CYCLE.length]),
  };
  const osprey: BoardHealthLane = {
    name: 'osprey',
    up: true,
    paused: true,
    lastSeenAt: new Date(TO).toISOString(),
    cells: Array.from({ length: BARS }, () => ({ state: 'paused', why: null })),
  };
  const heron: BoardHealthLane = {
    name: 'heron',
    up: false,
    paused: false,
    lastSeenAt: new Date(FROM).toISOString(),
    cells: Array.from({ length: BARS }, () => ({ state: 'off', why: null })),
  };
  const lanes = [kestrel, osprey, heron];

  return {
    up: lanes.filter((lane) => lane.up).length,
    paused: lanes.filter((lane) => lane.up && lane.paused).length,
    withLane: lanes.length,
    lanes,
    ...over,
  };
}

export function sampleHealth(over: Partial<BoardHealth> = {}): BoardHealth {
  const bars = Array.from({ length: BARS }, (_, i) => sampleBar(i));
  const total = bars.reduce((sum, bar) => sum + bar.moved, 0);

  return {
    from: new Date(FROM).toISOString(),
    to: new Date(TO).toISOString(),
    barMinutes: 20,
    bars,
    total,
    previousTotal: total - 16,
    pastNormal: 2,
    hasWorkColumn: true,
    runners: sampleRunners(),
    lastWaitedAt: null,
    lastWaitedIsFloor: false,
    ...over,
  };
}
