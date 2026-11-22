/* A hand-built board health answer for the component tests - imported by tests
   alone, so it never reaches the bundle. Bar `i` is known from `i`: moved is
   `i % 5`, in flight is `2 + i % 4`, and what waits on a person is `i % 3` pull
   requests plus `i % 2` questions. */

import { BARS } from './boardHealth';
import type { BoardHealth, BoardHealthBar } from '../types';

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
    ...over,
  };
}
