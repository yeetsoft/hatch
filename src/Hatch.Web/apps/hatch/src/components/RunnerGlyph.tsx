import type { ReactElement } from 'react';
import type { RunnerGlyphState } from '../lib/runners';

/** The glyph's own units. A 24-unit box, matching `Robot`. */
const SIZE = 24;

/** A ring, as the shape most states are built on. */
const RING = <circle cx="12" cy="12" r="8" fill="none" stroke="currentColor" strokeWidth="2.5" />;

/** What each state looks like. Shape carries the state - colour only agrees
    with it, so the row stays legible to a reader who cannot tell the inks apart. */
const SHAPES: Record<RunnerGlyphState, ReactElement> = {
  // A ring struck through: a runner that went missing.
  gone: (
    <>
      {RING}
      <line x1="5.5" y1="18.5" x2="18.5" y2="5.5" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />
    </>
  ),
  // A solid disc with a bar cut out of it: the way is shut until the cap resets.
  exhausted: <path fill="currentColor" fillRule="evenodd" d="M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0ZM7 10.75h10v2.5H7Z" />,
  // A ring with an exclamation mark: the agent itself, not the account, is the problem.
  'agent-down': (
    <>
      {RING}
      <rect x="10.75" y="6.5" width="2.5" height="7" rx="1.25" fill="currentColor" />
      <circle cx="12" cy="16.5" r="1.4" fill="currentColor" />
    </>
  ),
  // A half-filled disc: part of the way open, and it heals by itself.
  throttled: (
    <>
      {RING}
      <path fill="currentColor" d="M12 4.5a7.5 7.5 0 0 0 0 15Z" />
    </>
  ),
  // Two bars, for paused and stopping alike: the words say which.
  paused: (
    <>
      <rect x="6.5" y="5" width="4" height="14" rx="1" fill="currentColor" />
      <rect x="13.5" y="5" width="4" height="14" rx="1" fill="currentColor" />
    </>
  ),
  stopping: (
    <>
      <rect x="6.5" y="5" width="4" height="14" rx="1" fill="currentColor" />
      <rect x="13.5" y="5" width="4" height="14" rx="1" fill="currentColor" />
    </>
  ),
  working: <circle cx="12" cy="12" r="9" fill="currentColor" />,
  idle: RING,
  // A dashed ring: somewhere a runner could be, and is not yet.
  none: <circle cx="12" cy="12" r="8" fill="none" stroke="currentColor" strokeWidth="2.5" strokeDasharray="3.2 3.2" />,
};

/**
 * One runner's state, drawn as a shape: the list row's mark, and the one place
 * the state's words reach a screen reader, through `label`.
 *
 * Every part strokes or fills `currentColor`, so App.css picks the ink from the
 * state's class - danger, warn, primary, success or muted - and the box from
 * `.hatch-runner-glyph`.
 */
export function RunnerGlyph({ state, label }: { state: RunnerGlyphState; label: string }) {
  return (
    <svg
      className={`hatch-runner-glyph hatch-runner-glyph--${state}`}
      viewBox={`0 0 ${SIZE} ${SIZE}`}
      role="img"
      aria-label={label}
      data-state={state}
    >
      {SHAPES[state]}
    </svg>
  );
}
