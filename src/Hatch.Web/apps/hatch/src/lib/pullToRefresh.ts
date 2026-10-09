/**
 * The decisions behind the pull-down-to-refresh gesture, as plain functions.
 *
 * The hook (HA-326) is the wiring - it reads touch events, `useStandalone()`
 * and the DOM off the page and holds the gesture's running state. What those
 * readings mean is decided here, where it can be tested without a DOM: this
 * test runner has no `window` at all.
 */

/** Below this, a drag hasn't moved enough to be a deliberate pull yet. */
export const PULL_START_PX = 10;

/** How far a pull has to travel before letting go reloads the page. */
export const PULL_THRESHOLD_PX = 64;

export type PullPhase = 'idle' | 'pulling' | 'ready';

/** The phase a travelled distance (>= 0, the caller clamps) is in. */
export function pullPhase(distance: number): PullPhase {
  if (distance >= PULL_THRESHOLD_PX) return 'ready';
  if (distance >= PULL_START_PX) return 'pulling';
  return 'idle';
}

export interface PullSignals {
  standalone: boolean;
  atTop: boolean;
  dialogOpen: boolean;
}

/** Whether a gesture may begin at all, from the raw signals the caller already read off the document. */
export function mayStart({ standalone, atTop, dialogOpen }: PullSignals): boolean {
  return standalone && atTop && !dialogOpen;
}

/** Whether a move is vertical enough to be this gesture, rather than a sideways scroll. Ties (equal magnitude) are not vertical: a diagonal drag is left to the scroller it started in. */
export function isVerticalPull(deltaX: number, deltaY: number): boolean {
  return Math.abs(deltaY) > Math.abs(deltaX);
}

/** Whether letting go now reloads, given the phase the gesture was released in. */
export function mayRelease(phase: PullPhase): boolean {
  return phase === 'ready';
}

/** Whether a computed overflow-y value makes an element the one the hook's walk up the ancestors should stop at. */
export function isScroller(overflowY: string): boolean {
  return overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay';
}
